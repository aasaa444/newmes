using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class ProductionOrderLifecycleApiTests(SqlServerFixture server)
{
    private const string PlannerPassword = "IntegrationOnly-Planner-05!";
    private const string EngineerPassword = "IntegrationOnly-Engineer-05!";
    private const string OperatorPassword = "IntegrationOnly-Operator-05!";

    [SqlServerFact]
    public async Task ReleaseCreatesAnImmutableSelfContainedSnapshotUnaffectedByTemplateV2()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedActorsAndMaterialsAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var engineer = factory.CreateClient();
        await LoginAsync(engineer, "engineer.05", EngineerPassword);
        await AssertConflictAsync(
            await engineer.PostAsJsonAsync(
                "/api/process/execution-templates",
                new
                {
                    materialCode = "ROUTER-FG-01",
                    productVersion = "PRODUCT-INCOMPLETE",
                    version = "INCOMPLETE",
                    applicability = "缺失执行依据",
                }),
            422,
            "EXECUTION_TEMPLATE_INVALID",
            "执行模板必须完整包含版本化 BOM、路线、追溯、身份、固件、测试和完成门禁，且数量与工序顺序有效。");
        foreach (var invalidTemplate in new[]
                 {
                     TemplateRequest("INVALID-SOURCE", "ROUTE-INVALID", finishedSerialSource: " "),
                     TemplateRequest("INVALID-IDENTIFIER", "ROUTE-INVALID", requiredIdentifiers: [""]),
                     TemplateRequest("INVALID-GATE", "ROUTE-INVALID", completionRequirements: [" "]),
                 })
        {
            await AssertConflictAsync(
                await engineer.PostAsJsonAsync(
                    "/api/process/execution-templates",
                    invalidTemplate),
                422,
                "EXECUTION_TEMPLATE_INVALID",
                "执行模板必须完整包含版本化 BOM、路线、追溯、身份、固件、测试和完成门禁，且数量与工序顺序有效。");
        }
        await PublishTemplateAsync(engineer, "1.0", "ROUTE-1");

        using var planner = factory.CreateClient();
        await LoginAsync(planner, "planner.05", PlannerPassword);
        var orderId = await ReceiveOrderAsync(planner, "PO-LIFECYCLE-0501");
        var released = await planner.PostAsync(
            $"/api/planning/production-orders/{orderId}/release",
            null);
        released.EnsureSuccessStatusCode();
        var releaseResult = await released.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Released", releaseResult.GetProperty("status").GetString());
        Assert.Equal("1.0", releaseResult.GetProperty("snapshotVersion").GetString());

        var before = await planner.GetStringAsync(
            $"/api/planning/production-orders/{orderId}/execution-snapshot");
        var snapshot = JsonDocument.Parse(before).RootElement;
        Assert.Equal("ROUTER-FG-01", snapshot.GetProperty("product").GetProperty("materialCode").GetString());
        Assert.Equal("PRODUCT-1.0", snapshot.GetProperty("product").GetProperty("sourceVersion").GetString());
        Assert.Equal("BOM-1.0", snapshot.GetProperty("bom").GetProperty("version").GetString());
        Assert.Equal(2, snapshot.GetProperty("bom").GetProperty("components").GetArrayLength());
        Assert.Equal("ROUTE-1", snapshot.GetProperty("route").GetProperty("version").GetString());
        Assert.Equal(6, snapshot.GetProperty("route").GetProperty("operations").GetArrayLength());
        Assert.Equal("TRACE-1.0", snapshot.GetProperty("traceabilityPolicy").GetProperty("version").GetString());
        Assert.Equal("IDENTITY-1.0", snapshot.GetProperty("identityPolicy").GetProperty("version").GetString());
        Assert.Single(snapshot.GetProperty("firmwareRequirements").EnumerateArray());
        Assert.Single(snapshot.GetProperty("testSpecifications").EnumerateArray());
        Assert.Equal("GATE-1.0", snapshot.GetProperty("completionGate").GetProperty("version").GetString());

        await PublishTemplateAsync(engineer, "2.0", "ROUTE-2");
        Assert.Equal(
            before,
            await planner.GetStringAsync(
                $"/api/planning/production-orders/{orderId}/execution-snapshot"));

        await using var context = CreateContext(connectionString);
        Assert.Equal(2, await ScalarAsync(context, "SELECT COUNT(*) FROM [mes].[ProductExecutionTemplateVersions]"));
        Assert.Equal(1, await ScalarAsync(context, "SELECT COUNT(*) FROM [mes].[ProductionOrderExecutionSnapshots]"));
        var templateError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("""
                UPDATE [mes].[ProductExecutionTemplateVersions]
                SET [Version] = N'forbidden';
                """));
        var snapshotError = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE [mes].[ProductionOrderExecutionSnapshots]
            SET [SnapshotVersion] = N'forbidden'
            WHERE [ProductionOrderId] = {{orderId}};
            """));
        Assert.Equal(51007, templateError.Number);
        Assert.Equal(51008, snapshotError.Number);
    }

    [SqlServerFact]
    public async Task ReleaseRequiresACompleteTemplateAndConcurrentRequestsConverge()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedActorsAndMaterialsAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var planner = factory.CreateClient();
        await LoginAsync(planner, "planner.05", PlannerPassword);
        var orderId = await ReceiveOrderAsync(planner, "PO-CONCURRENT-0502");

        var missingTemplate = await planner.PostAsync(
            $"/api/planning/production-orders/{orderId}/release",
            null);
        await AssertConflictAsync(
            missingTemplate,
            422,
            "EXECUTION_TEMPLATE_NOT_FOUND",
            "该成品没有已批准且完整的执行模板；请由工艺工程师发布版本后再下达。");

        using var engineer = factory.CreateClient();
        await LoginAsync(engineer, "engineer.05", EngineerPassword);
        await PublishTemplateAsync(engineer, "1.0", "ROUTE-1");

        Guid legacyOrderId;
        await using (var legacyContext = CreateContext(connectionString))
        {
            legacyOrderId = Guid.NewGuid();
            legacyContext.ProductionOrders.Add(new ProductionOrder
            {
                Id = legacyOrderId,
                OrderNumber = "LEGACY-INCOMPLETE-0502",
                MaterialId = await legacyContext.Materials
                    .Where(material => material.Code == "ROUTER-FG-01")
                    .Select(material => material.Id)
                    .SingleAsync(),
                PlannedQuantity = 10,
                Status = ProductionOrderStatus.Received,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await legacyContext.SaveChangesAsync();
        }
        await AssertConflictAsync(
            await planner.PostAsync(
                $"/api/planning/production-orders/{legacyOrderId}/release",
                null),
            422,
            "PRODUCTION_ORDER_INCOMPLETE",
            "生产订单缺少可证明的来源系统、业务引用或来源版本，不能下达；请通过受控入站补齐新订单。");
        var workbench = await planner.GetFromJsonAsync<JsonElement>(
            "/api/planning/production-orders");
        var legacyOrder = workbench.GetProperty("orders")
            .EnumerateArray()
            .Single(order => order.GetProperty("id").GetGuid() == legacyOrderId);
        var availableCommands = legacyOrder.GetProperty("availableCommands")
            .EnumerateArray()
            .Select(command => command.GetString())
            .ToArray();
        Assert.Single(availableCommands);
        Assert.Equal("Cancel", availableCommands[0]);

        var responses = await Task.WhenAll(
            planner.PostAsync($"/api/planning/production-orders/{orderId}/release", null),
            planner.PostAsync($"/api/planning/production-orders/{orderId}/release", null));
        Assert.All(responses, response => response.EnsureSuccessStatusCode());
        Assert.Single((await Task.WhenAll(
            responses.Select(response => response.Content.ReadAsStringAsync())))
            .Distinct(StringComparer.Ordinal));

        await using var context = CreateContext(connectionString);
        Assert.Equal(1, await ScalarAsync(context, "SELECT COUNT(*) FROM [mes].[ProductionOrderExecutionSnapshots]"));
        Assert.Equal(
            1,
            await context.BusinessAuditRecords.CountAsync(audit =>
                audit.Action == "PRODUCTION_ORDER_RELEASE"
                && audit.Result == BusinessAuditResult.Succeeded));
    }

    [SqlServerFact]
    public async Task LifecycleCommandsEnforceStateQuantityAndCrossSystemClosureGates()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedActorsAndMaterialsAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var engineer = factory.CreateClient();
        await LoginAsync(engineer, "engineer.05", EngineerPassword);
        await PublishTemplateAsync(engineer, "1.0", "ROUTE-1");
        using var planner = factory.CreateClient();
        await LoginAsync(planner, "planner.05", PlannerPassword);
        var orderId = await ReceiveOrderAsync(planner, "PO-LIFECYCLE-0503");
        (await planner.PostAsync($"/api/planning/production-orders/{orderId}/release", null))
            .EnsureSuccessStatusCode();

        await AssertConflictAsync(
            await planner.PostAsync($"/api/planning/production-orders/{orderId}/pause", null),
            409,
            "PRODUCTION_ORDER_TRANSITION_NOT_ALLOWED",
            "当前订单状态不允许执行该命令；请刷新工作台并按可执行命令处理。");

        await using (var context = CreateContext(connectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ProductionOrders]
                SET [Status] = N'InProduction', [StartedQuantity] = 3,
                    [QualifiedQuantity] = 1, [ScrappedQuantity] = 0
                WHERE [Id] = {{orderId}};
                """);
        }

        (await planner.PostAsync($"/api/planning/production-orders/{orderId}/pause", null))
            .EnsureSuccessStatusCode();
        await AssertConflictAsync(
            await planner.PostAsync($"/api/planning/production-orders/{orderId}/cancel", null),
            409,
            "PRODUCTION_ORDER_CANNOT_CANCEL_AFTER_START",
            "该订单已经投产，不能取消；请暂停并通过受控完成或变更流程处理。");
        (await planner.PostAsync($"/api/planning/production-orders/{orderId}/resume", null))
            .EnsureSuccessStatusCode();
        await AssertConflictAsync(
            await planner.PostAsync($"/api/planning/production-orders/{orderId}/complete-execution", null),
            409,
            "PRODUCTION_ORDER_QUANTITY_NOT_TERMINAL",
            "尚无投产记录或仍有在制数量，不能执行完成；请先处理全部已投产产品。");

        await using (var context = CreateContext(connectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ProductionOrders]
                SET [QualifiedQuantity] = 2, [ScrappedQuantity] = 1
                WHERE [Id] = {{orderId}};
                """);
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ProductionOrders]
                SET [QualifiedQuantity] = 11
                WHERE [Id] = {{orderId}};
                """));
        }

        (await planner.PostAsync(
            $"/api/planning/production-orders/{orderId}/complete-execution",
            null)).EnsureSuccessStatusCode();
        await AssertConflictAsync(
            await planner.PostAsync($"/api/planning/production-orders/{orderId}/close", null),
            409,
            "PRODUCTION_ORDER_CLOSURE_NOT_RECONCILED",
            "制造执行已完成，但仓储交接和 ERP 对账尚未同时完成，不能关闭订单。");

        await using (var context = CreateContext(connectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ProductionOrders]
                SET [WarehouseHandoffCompleted] = 1, [ErpReconciled] = 1
                WHERE [Id] = {{orderId}};
                """);
        }
        var closed = await planner.PostAsync(
            $"/api/planning/production-orders/{orderId}/close",
            null);
        closed.EnsureSuccessStatusCode();
        var result = await closed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", result.GetProperty("status").GetString());

        var workbench = await planner.GetFromJsonAsync<JsonElement>(
            "/api/planning/production-orders");
        var order = Assert.Single(workbench.GetProperty("orders").EnumerateArray());
        Assert.Equal(10, order.GetProperty("plannedQuantity").GetInt32());
        Assert.Equal(3, order.GetProperty("startedQuantity").GetInt32());
        Assert.Equal(2, order.GetProperty("qualifiedQuantity").GetInt32());
        Assert.Equal(1, order.GetProperty("scrappedQuantity").GetInt32());
        Assert.Equal(7, order.GetProperty("unstartedQuantity").GetInt32());
        Assert.Equal(0, order.GetProperty("wipQuantity").GetInt32());
        Assert.Empty(order.GetProperty("availableCommands").EnumerateArray());
    }

    [SqlServerFact]
    public async Task ReceivedOrderCanCancelButOperatorCannotPublishOrManageLifecycle()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedActorsAndMaterialsAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var planner = factory.CreateClient();
        await LoginAsync(planner, "planner.05", PlannerPassword);
        var orderId = await ReceiveOrderAsync(planner, "PO-CANCEL-0504");
        var cancelled = await planner.PostAsync(
            $"/api/planning/production-orders/{orderId}/cancel",
            null);
        cancelled.EnsureSuccessStatusCode();
        Assert.Equal(
            "Cancelled",
            (await cancelled.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("status").GetString());

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator.05", OperatorPassword);
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            (await operatorClient.PostAsJsonAsync(
                "/api/process/execution-templates",
                TemplateRequest("1.0", "ROUTE-1"))).StatusCode);
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            (await operatorClient.PostAsync(
                $"/api/planning/production-orders/{orderId}/release",
                null)).StatusCode);
    }

    private static object TemplateRequest(
        string version,
        string routeVersion,
        string finishedSerialSource = "AuthorizedExternalOrControlledPool",
        string[]? requiredIdentifiers = null,
        string[]? completionRequirements = null) => new
        {
            materialCode = "ROUTER-FG-01",
            productVersion = $"PRODUCT-{version}",
            version,
            applicability = "工业路由器整机装配技术验证样板，待现场确认",
            bom = new
            {
                version = $"BOM-{version}",
                components = new object[]
            {
                new { materialCode = "ROUTER-PCBA-01", quantityPer = 1m, unit = "EA", traceabilityMode = "Serial", assemblyOperationCode = "ASSEMBLY_BIND", consumptionRule = "PerProductActual" },
                new { materialCode = "ROUTER-ENCLOSURE-01", quantityPer = 1m, unit = "EA", traceabilityMode = "Lot", assemblyOperationCode = "ASSEMBLY_BIND", consumptionRule = "PerProductActual" },
            },
            },
            route = new
            {
                version = routeVersion,
                operations = new object[]
            {
                new { sequence = 10, code = "START_WIP", name = "投入生产" },
                new { sequence = 20, code = "ASSEMBLY_BIND", name = "装配绑定" },
                new { sequence = 30, code = "FW_CONFIG", name = "固件配置" },
                new { sequence = 40, code = "FUNCTION_TEST", name = "功能测试" },
                new { sequence = 50, code = "FINAL_INSPECTION", name = "终检" },
                new { sequence = 60, code = "COMPLETE", name = "制造完工" },
            },
            },
            traceabilityPolicy = new
            {
                version = $"TRACE-{version}",
                finishedProductMode = "Serial",
            },
            identityPolicy = new
            {
                version = $"IDENTITY-{version}",
                finishedSerialSource,
                requiredIdentifiers = requiredIdentifiers ?? ["SerialNumber"],
            },
            firmwareRequirements = new[]
        {
            new
            {
                code = "FW-BASELINE",
                version = $"FWREQ-{version}",
                required = true,
                evidenceReference = "客户批准的固件基线，具体版本待现场确认",
            },
        },
            testSpecifications = new[]
        {
            new
            {
                code = "FUNCTION-TEST",
                version = $"TEST-{version}",
                required = true,
                evidenceReference = "客户批准的功能测试规范，阈值待现场确认",
            },
        },
            completionGate = new
            {
                version = $"GATE-{version}",
                requirements = completionRequirements ??
            [
                "ROUTE_COMPLETE",
                "MATERIAL_COMPLETE",
                "FIRMWARE_COMPLETE",
                "TESTS_PASSED",
                "FINAL_INSPECTION_PASSED",
                "NO_OPEN_QUALITY_HOLD",
            ],
            },
        };

    private static async Task PublishTemplateAsync(
        HttpClient client,
        string version,
        string routeVersion)
    {
        var response = await client.PostAsJsonAsync(
            "/api/process/execution-templates",
            TemplateRequest(version, routeVersion));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> ReceiveOrderAsync(HttpClient client, string orderNumber)
    {
        var response = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            new
            {
                sourceSystem = "ERP-U8",
                messageId = $"MSG-{orderNumber}",
                businessKey = orderNumber,
                sourceVersion = "1",
                contractVersion = "1.0",
                orderNumber,
                materialCode = "ROUTER-FG-01",
                plannedQuantity = 10,
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("productionOrderId").GetGuid();
    }

    private static async Task AssertConflictAsync(
        HttpResponseMessage response,
        int statusCode,
        string code,
        string message)
    {
        Assert.Equal(statusCode, (int)response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, payload.GetProperty("code").GetString());
        Assert.Equal(message, payload.GetProperty("message").GetString());
    }

    private static async Task SeedActorsAndMaterialsAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        context.UserAccounts.AddRange(
            Account("planner.05", "计划员 05", PlannerPassword, BusinessRole.Planner),
            Account("engineer.05", "工艺工程师 05", EngineerPassword, BusinessRole.ProcessEngineer),
            Account("operator.05", "操作工 05", OperatorPassword, BusinessRole.Operator));
        context.Materials.AddRange(
            Material("ROUTER-FG-01", "工业路由器成品", TraceabilityMode.Serial),
            Material("ROUTER-PCBA-01", "已测 PCBA", TraceabilityMode.Serial),
            Material("ROUTER-ENCLOSURE-01", "路由器外壳", TraceabilityMode.Lot));
        await context.SaveChangesAsync();
    }

    private static UserAccount Account(
        string username,
        string displayName,
        string password,
        BusinessRole role)
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            IsActive = true,
            PrimaryRole = role,
        };
        account.PasswordHash = new PasswordHasher<UserAccount>().HashPassword(account, password);
        account.RoleAssignments.Add(new UserRoleAssignment
        {
            UserAccountId = account.Id,
            Role = role,
            UserAccount = account,
        });
        return account;
    }

    private static Material Material(string code, string name, TraceabilityMode mode) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        TraceabilityMode = mode,
        IsActive = true,
    };

    private static async Task<int> ScalarAsync(MesDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__MesDatabase", connectionString);
        Environment.SetEnvironmentVariable(
            "Security__JwtSigningKey",
            "IntegrationOnlySigningKey_05_AtLeast32Characters");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            payload.GetProperty("accessToken").GetString());
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }
}
