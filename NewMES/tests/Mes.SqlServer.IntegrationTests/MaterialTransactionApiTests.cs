using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Materials;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class MaterialTransactionApiTests(SqlServerFixture server)
{
    private const string HandlerPassword = "IntegrationOnly-Handler-06!";
    private const string OperatorPassword = "IntegrationOnly-Operator-06!";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task TransferIssueAndWorkbenchKeepDistinctIdempotentMaterialFacts()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var handler = factory.CreateClient();
        await LoginAsync(handler, "handler.06", HandlerPassword);
        var occurredAt = new DateTimeOffset(2026, 7, 29, 8, 0, 0, TimeSpan.Zero);
        var transfer = TransferRequest("TRANSFER-0601", "LOT-A", 15, "EA", occurredAt);

        var accepted = await handler.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            transfer);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var acceptedResult = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var transferId = acceptedResult.GetProperty("transactionId").GetGuid();
        Assert.False(acceptedResult.GetProperty("isReplay").GetBoolean());
        Assert.Equal(15m, acceptedResult.GetProperty("lineSideBalance").GetDecimal());

        var replay = await handler.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            transfer);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(transferId, replayResult.GetProperty("transactionId").GetGuid());
        Assert.True(replayResult.GetProperty("isReplay").GetBoolean());

        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                "/api/material/line-side-transfers",
                TransferRequest("TRANSFER-0601", "LOT-A", 14, "EA", occurredAt)),
            409,
            "MATERIAL_IDEMPOTENCY_CONFLICT");
        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                "/api/material/line-side-transfers",
                TransferRequest("TRANSFER-UNIT-0601", "LOT-A", 1, "KG", occurredAt)),
            422,
            "MATERIAL_UNIT_MISMATCH");

        var issued = await handler.PostAsJsonAsync(
            "/api/material/order-issues",
            OrderRequest("ISSUE-0601", setup.OrderId, "LOT-A", 12, null, occurredAt));
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var issuedResult = await issued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3m, issuedResult.GetProperty("lineSideBalance").GetDecimal());
        Assert.Equal(12m, issuedResult.GetProperty("orderAvailableBalance").GetDecimal());

        (await handler.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            TransferRequest("TRANSFER-0602", "LOT-B", 10, "EA", occurredAt)))
            .EnsureSuccessStatusCode();
        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                "/api/material/order-issues",
                OrderRequest("ISSUE-OVER-0601", setup.OrderId, "LOT-B", 9, null, occurredAt)),
            409,
            "ORDER_MATERIAL_OVER_ISSUE");
        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                "/api/material/order-issues",
                OrderRequest("ISSUE-NEGATIVE-0601", setup.OrderId, "LOT-A", 4, null, occurredAt)),
            409,
            "LINE_SIDE_INVENTORY_INSUFFICIENT");

        var workbenchResponse = await handler.GetAsync(
            $"/api/material/workbench?materialCode=ROUTER-PCBA-01&productionOrderId={setup.OrderId}");
        var workbenchBody = await workbenchResponse.Content.ReadAsStringAsync();
        Assert.True(workbenchResponse.IsSuccessStatusCode, workbenchBody);
        var workbench = JsonDocument.Parse(workbenchBody).RootElement;
        var lineBalances = workbench.GetProperty("lineSideBalances").EnumerateArray().ToArray();
        Assert.Equal(2, lineBalances.Length);
        Assert.Contains(
            lineBalances,
            balance => balance.GetProperty("lotNumber").GetString() == "LOT-A"
                && balance.GetProperty("quantity").GetDecimal() == 3m);
        var orderBalance = Assert.Single(workbench.GetProperty("orderBalances").EnumerateArray());
        Assert.Equal(12m, orderBalance.GetProperty("availableQuantity").GetDecimal());
        Assert.Equal(12m, orderBalance.GetProperty("netIssuedQuantity").GetDecimal());
        Assert.DoesNotContain(
            workbench.GetProperty("transactions").EnumerateArray(),
            item => item.GetProperty("transactionType").GetString() == "Consumption");

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator.06", OperatorPassword);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.GetAsync("/api/material/workbench")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.PostAsJsonAsync(
                "/api/material/line-side-transfers",
                TransferRequest("OPERATOR-DENIED", "LOT-A", 1, "EA", occurredAt))).StatusCode);
    }

    [SqlServerFact]
    public async Task ConcurrentIssuesCannotOverdrawOneLineSideLot()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await LoginAsync(firstClient, "handler.06", HandlerPassword);
        await LoginAsync(secondClient, "handler.06", HandlerPassword);
        var occurredAt = new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero);
        (await firstClient.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            TransferRequest("TRANSFER-CONCURRENT-06", "LOT-C", 10, "EA", occurredAt)))
            .EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                "/api/material/order-issues",
                OrderRequest("ISSUE-CONCURRENT-A", setup.OrderId, "LOT-C", 7, null, occurredAt)),
            secondClient.PostAsJsonAsync(
                "/api/material/order-issues",
                OrderRequest("ISSUE-CONCURRENT-B", setup.OrderId, "LOT-C", 7, null, occurredAt)));
        Assert.Single(responses, response => response.IsSuccessStatusCode);
        var rejected = Assert.Single(responses, response => !response.IsSuccessStatusCode);
        await AssertRejectedAsync(rejected, 409, "LINE_SIDE_INVENTORY_INSUFFICIENT");

        await using var context = CreateContext(setup.ConnectionString);
        Assert.Equal(
            7m,
            await context.MaterialTransactions
                .Where(item => item.TransactionType == MaterialTransactionType.OrderIssue)
                .SumAsync(item => item.Quantity));
        Assert.Equal(
            3m,
            await context.MaterialTransactions
                .Where(item => item.LotNumber == "LOT-C")
                .SumAsync(item => item.LineSideQuantityDelta));
    }

    [SqlServerFact]
    public async Task ReturnReversalAdjustmentAndDatabaseFailureRemainAppendOnlyAndAtomic()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var handler = factory.CreateClient();
        await LoginAsync(handler, "handler.06", HandlerPassword);
        var occurredAt = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);
        (await handler.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            TransferRequest("TRANSFER-RETURN-06", "LOT-D", 10, "EA", occurredAt)))
            .EnsureSuccessStatusCode();
        var issue = await handler.PostAsJsonAsync(
            "/api/material/order-issues",
            OrderRequest("ISSUE-RETURN-06", setup.OrderId, "LOT-D", 6, null, occurredAt));
        issue.EnsureSuccessStatusCode();

        var returned = await handler.PostAsJsonAsync(
            "/api/material/order-returns",
            OrderRequest("RETURN-0601", setup.OrderId, "LOT-D", 2, "工单余料退回线边", occurredAt));
        returned.EnsureSuccessStatusCode();
        var returnId = (await returned.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("transactionId")
            .GetGuid();
        var reversed = await handler.PostAsJsonAsync(
            $"/api/material/transactions/{returnId}/reverse",
            ReversalRequest("REVERSE-RETURN-0601", "退料数量录入错误", occurredAt));
        reversed.EnsureSuccessStatusCode();
        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                $"/api/material/transactions/{returnId}/reverse",
                ReversalRequest("REVERSE-RETURN-0602", "重复冲正验证", occurredAt)),
            409,
            "MATERIAL_TRANSACTION_NOT_REVERSIBLE");

        var adjusted = await handler.PostAsJsonAsync(
            "/api/material/adjustments",
            AdjustmentRequest("ADJUST-0601", "LOT-D", 3, "盘点确认增加", occurredAt));
        adjusted.EnsureSuccessStatusCode();
        await AssertRejectedAsync(
            await handler.PostAsJsonAsync(
                "/api/material/adjustments",
                AdjustmentRequest("ADJUST-NEGATIVE-0601", "LOT-D", -8, "盘点确认减少", occurredAt)),
            409,
            "LINE_SIDE_INVENTORY_INSUFFICIENT");

        await using (var context = CreateContext(setup.ConnectionString))
        {
            var updateError = await Assert.ThrowsAsync<SqlException>(() =>
                context.Database.ExecuteSqlRawAsync("""
                    UPDATE [mes].[MaterialTransactions]
                    SET [Reason] = N'forbidden';
                    """));
            Assert.Equal(51009, updateError.Number);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [audit].[TR_Ticket06_ForceAuditFailure]
                ON [audit].[BusinessAuditRecords]
                AFTER INSERT
                AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [Action] = 'MATERIAL_LINE_SIDE_TRANSFER')
                        THROW 51906, 'Ticket 06 forced rollback.', 1;
                END;
                """);
        }

        var failed = await handler.PostAsJsonAsync(
            "/api/material/line-side-transfers",
            TransferRequest("TRANSFER-ROLLBACK-06", "LOT-ROLLBACK", 5, "EA", occurredAt));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        await using (var context = CreateContext(setup.ConnectionString))
        {
            Assert.False(await context.MaterialTransactions.AnyAsync(
                item => item.IdempotencyKey == "TRANSFER-ROLLBACK-06"));
            Assert.False(await context.BusinessAuditRecords.AnyAsync(
                item => item.Action == "MATERIAL_LINE_SIDE_TRANSFER"
                    && item.Result == BusinessAuditResult.Succeeded
                    && item.BusinessObjectId.Contains("TRANSFER-ROLLBACK-06")));
        }
    }

    private static object TransferRequest(
        string idempotencyKey,
        string lotNumber,
        decimal quantity,
        string unit,
        DateTimeOffset occurredAtUtc) => new
        {
            contractVersion = "1.0",
            sourceSystem = "WMS-DEMO",
            idempotencyKey,
            sourceDocumentType = "LINE_SIDE_TRANSFER",
            sourceDocumentNumber = $"DOC-{idempotencyKey}",
            materialCode = "ROUTER-PCBA-01",
            lotNumber,
            quantity,
            unit,
            fromParty = "原材料仓",
            toParty = "总装线边区",
            occurredAtUtc,
        };

    private static object OrderRequest(
        string idempotencyKey,
        Guid productionOrderId,
        string lotNumber,
        decimal quantity,
        string? reason,
        DateTimeOffset occurredAtUtc) => new
        {
            contractVersion = "1.0",
            sourceSystem = "MES-MATERIAL-WORKBENCH",
            idempotencyKey,
            productionOrderId,
            materialCode = "ROUTER-PCBA-01",
            lotNumber,
            quantity,
            unit = "EA",
            sourceDocumentNumber = $"PICK-{idempotencyKey}",
            reason,
            occurredAtUtc,
        };

    private static object AdjustmentRequest(
        string idempotencyKey,
        string lotNumber,
        decimal quantityDelta,
        string reason,
        DateTimeOffset occurredAtUtc) => new
        {
            contractVersion = "1.0",
            sourceSystem = "MES-MATERIAL-WORKBENCH",
            idempotencyKey,
            materialCode = "ROUTER-PCBA-01",
            lotNumber,
            quantityDelta,
            unit = "EA",
            reason,
            occurredAtUtc,
        };

    private static object ReversalRequest(
        string idempotencyKey,
        string reason,
        DateTimeOffset occurredAtUtc) => new
        {
            contractVersion = "1.0",
            sourceSystem = "MES-MATERIAL-WORKBENCH",
            idempotencyKey,
            reason,
            occurredAtUtc,
        };

    private static async Task AssertRejectedAsync(
        HttpResponseMessage response,
        int statusCode,
        string code)
    {
        Assert.Equal(statusCode, (int)response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, payload.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("message").GetString()));
    }

    private static async Task<TestSetup> CreateSetupAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var handler = Account("handler.06", "物料交接员 06", HandlerPassword, BusinessRole.MaterialHandler);
        var operatorAccount = Account("operator.06", "操作工 06", OperatorPassword, BusinessRole.Operator);
        var finished = Material("ROUTER-FG-01", "工业路由器成品", TraceabilityMode.Serial);
        var component = Material("ROUTER-PCBA-01", "已测 PCBA", TraceabilityMode.Serial);
        context.AddRange(handler, operatorAccount, finished, component);
        await context.SaveChangesAsync();

        var orderId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var definition = Definition();
        var definitionJson = JsonSerializer.Serialize(definition, WebJson);
        context.ProductExecutionTemplateVersions.Add(new ProductExecutionTemplateVersion
        {
            Id = templateId,
            MaterialId = finished.Id,
            Version = "1.0",
            Applicability = "Ticket 06 物料事务技术验证夹具",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('0', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = new DateTimeOffset(2026, 7, 29, 7, 0, 0, TimeSpan.Zero),
            PublishedByUserId = handler.Id,
        });
        context.ProductionOrders.Add(new ProductionOrder
        {
            Id = orderId,
            OrderNumber = "PO-MATERIAL-0601",
            MaterialId = finished.Id,
            PlannedQuantity = 10,
            Status = ProductionOrderStatus.Released,
            CreatedAtUtc = new DateTimeOffset(2026, 7, 29, 7, 0, 0, TimeSpan.Zero),
            ReleasedAtUtc = new DateTimeOffset(2026, 7, 29, 7, 30, 0, TimeSpan.Zero),
            SourceSystem = "ERP-U8",
            SourceReference = "PO-MATERIAL-0601",
            SourceVersion = "1",
        });
        await context.SaveChangesAsync();
        context.ProductionOrderExecutionSnapshots.Add(new ProductionOrderExecutionSnapshot
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = orderId,
            SourceTemplateId = templateId,
            SnapshotVersion = "1.0",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('0', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            CreatedAtUtc = new DateTimeOffset(2026, 7, 29, 7, 30, 0, TimeSpan.Zero),
            CreatedByUserId = handler.Id,
        });
        await context.SaveChangesAsync();
        return new TestSetup(connectionString, orderId);
    }

    private static ExecutionTemplateDefinition Definition() => new(
        new ProductDefinition("ROUTER-FG-01", "工业路由器成品", "PRODUCT-1.0", "Serial"),
        "Ticket 06 技术验证夹具",
        new BomDefinition(
            "BOM-1.0",
            [new BomComponentDefinition("ROUTER-PCBA-01", 2m, "EA", "Serial")]),
        new RouteDefinition("ROUTE-1.0", [new RouteOperationDefinition(10, "START_WIP", "投入生产")]),
        new TraceabilityPolicyDefinition("TRACE-1.0", "Serial"),
        new IdentityPolicyDefinition("IDENTITY-1.0", "ControlledPool", ["SerialNumber"]),
        [new FirmwareRequirementDefinition("FW", "1.0", true, "fixture")],
        [new TestSpecificationReferenceDefinition("TEST", "1.0", true, "fixture")],
        new CompletionGateDefinition("GATE-1.0", ["ROUTE_COMPLETE"]));

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
        BaseUnit = "EA",
        TraceabilityMode = mode,
        IsActive = true,
    };

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__MesDatabase", connectionString);
        Environment.SetEnvironmentVariable(
            "Security__JwtSigningKey",
            "IntegrationOnlySigningKey_06_AtLeast32Characters");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
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

    private sealed record TestSetup(string ConnectionString, Guid OrderId);
}
