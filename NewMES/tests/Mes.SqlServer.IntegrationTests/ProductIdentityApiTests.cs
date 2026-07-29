using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
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
public sealed class ProductIdentityApiTests(SqlServerFixture server)
{
    private const string AdministratorPassword = "IntegrationOnly-Administrator-07!";
    private const string OperatorPassword = "IntegrationOnly-Operator-07!";
    private const string PlannerPassword = "IntegrationOnly-Planner-07!";

    [SqlServerFact]
    public async Task AuthorizedIdentityStartsWipOnceAndExposesWorkstationContext()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 2);
        await using var factory = CreateFactory(connectionString);

        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var registeredSource = await administrator.PostAsJsonAsync(
            "/api/configuration/identity-sources",
            new
            {
                sourceSystem = "LABEL-SYSTEM-07",
                sourceType = "LabelSystem",
                authorizedCallerUsername = "administrator.07",
                allowedIdentifierTypes = (string[])["SerialNumber"],
                authorizationEvidence = "客户批准的标签系统接口清单 07",
            });
        Assert.Equal(System.Net.HttpStatusCode.Created, registeredSource.StatusCode);
        var allocation = await administrator.PostAsJsonAsync(
            "/api/integration/product-identities",
            IdentityRequest("ALLOC-0701", "SN-ROUTER-0701"));
        Assert.Equal(System.Net.HttpStatusCode.Created, allocation.StatusCode);
        var allocated = await allocation.Content.ReadFromJsonAsync<JsonElement>();
        var identityId = allocated.GetProperty("productIdentityId").GetGuid();
        Assert.Equal("Erp", allocated.GetProperty("serialSourceType").GetString());
        Assert.False(allocated.GetProperty("isDemo").GetBoolean());

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator.07", OperatorPassword);
        var request = new
        {
            sourceSystem = "MES-STATION",
            idempotencyKey = "START-0701",
            productIdentityId = identityId,
            location = "LINE-01",
            occurredAtUtc = DateTimeOffset.UtcNow,
        };
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            (await administrator.PostAsJsonAsync(
                $"/api/execution/production-orders/{orderId}/start-wip",
                request)).StatusCode);
        var first = await operatorClient.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            request);
        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(firstResult.GetProperty("isReplay").GetBoolean());
        Assert.Equal("InProduction", firstResult.GetProperty("orderStatus").GetString());
        Assert.Equal("ASSEMBLY_BIND", firstResult.GetProperty("nextOperationCode").GetString());

        var replay = await operatorClient.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            request);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isReplay").GetBoolean());

        var workstation = await operatorClient.GetAsync(
            "/api/execution/product-identities/by-serial/SN-ROUTER-0701/workstation");
        workstation.EnsureSuccessStatusCode();
        var context = await workstation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SN-ROUTER-0701", context.GetProperty("serialNumber").GetString());
        Assert.Equal("ERP-U8", context.GetProperty("identitySourceSystem").GetString());
        Assert.Equal("PO-IDENTITY-0701", context.GetProperty("productionOrderNumber").GetString());
        Assert.Equal("ASSEMBLY_BIND", context.GetProperty("nextOperationCode").GetString());

        await using var db = CreateContext(connectionString);
        var order = await db.ProductionOrders.SingleAsync(item => item.Id == orderId);
        Assert.Equal(1, order.StartedQuantity);
        Assert.Equal(ProductionOrderStatus.InProduction, order.Status);
        Assert.Equal(1, await db.ManufacturingEvents.CountAsync(item => item.EventType == "IDENTITY_ALLOCATED"));
        Assert.Equal(1, await db.ManufacturingEvents.CountAsync(item => item.EventType == "IDENTITY_BOUND"));
        Assert.Equal(1, await db.ManufacturingEvents.CountAsync(item => item.EventType == "START_WIP"));

        using var anonymous = factory.CreateClient();
        Assert.Equal(
            System.Net.HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(
                "/api/integration/product-identities",
                IdentityRequest("ANONYMOUS-0701", "SN-ANONYMOUS-0701"))).StatusCode);
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            (await operatorClient.PostAsJsonAsync(
                "/api/integration/product-identities",
                IdentityRequest("OPERATOR-0701", "SN-OPERATOR-0701"))).StatusCode);
    }

    [SqlServerFact]
    public async Task ConcurrentStartsRespectPlanAndPausedOrderRejectsNewWip()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 1);
        await using var factory = CreateFactory(connectionString);
        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var firstIdentity = await AllocateAsync(
            administrator,
            IdentityRequest("ALLOC-0702-A", "SN-ROUTER-0702-A", "00:11:22:33:44:56"));
        var secondIdentity = await AllocateAsync(
            administrator,
            IdentityRequest("ALLOC-0702-B", "SN-ROUTER-0702-B", "00:11:22:33:44:57"));

        using var stationA = factory.CreateClient();
        using var stationB = factory.CreateClient();
        await LoginAsync(stationA, "operator.07", OperatorPassword);
        await LoginAsync(stationB, "operator.07", OperatorPassword);
        var responses = await Task.WhenAll(
            stationA.PostAsJsonAsync(
                $"/api/execution/production-orders/{orderId}/start-wip",
                StartRequest("START-0702-A", firstIdentity)),
            stationB.PostAsJsonAsync(
                $"/api/execution/production-orders/{orderId}/start-wip",
                StartRequest("START-0702-B", secondIdentity)));
        Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.Created);
        var rejected = Assert.Single(
            responses,
            response => response.StatusCode == System.Net.HttpStatusCode.Conflict);
        Assert.Equal(
            "START_WIP_PLANNED_QUANTITY_EXCEEDED",
            (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using (var db = CreateContext(connectionString))
        {
            var order = await db.ProductionOrders.SingleAsync(item => item.Id == orderId);
            Assert.Equal(1, order.StartedQuantity);
            Assert.Equal(1, await db.ManufacturingEvents.CountAsync(item => item.EventType == "START_WIP"));
        }

        var secondOrderId = await AddReleasedOrderAsync(
            connectionString,
            "PO-IDENTITY-PAUSED-0702",
            plannedQuantity: 1);
        using var planner = factory.CreateClient();
        await LoginAsync(planner, "planner.07", PlannerPassword);
        (await planner.PostAsync(
            $"/api/planning/production-orders/{secondOrderId}/pause",
            null)).EnsureSuccessStatusCode();
        var thirdIdentity = await AllocateAsync(
            administrator,
            IdentityRequest("ALLOC-0702-C", "SN-ROUTER-0702-C", "00:11:22:33:44:58"));
        var paused = await stationA.PostAsJsonAsync(
            $"/api/execution/production-orders/{secondOrderId}/start-wip",
            StartRequest("START-0702-C", thirdIdentity));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, paused.StatusCode);
        Assert.Equal(
            "START_WIP_ORDER_STATUS_BLOCKED",
            (await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task SnapshotIdentifiersAndGlobalIdentityUniquenessAreEnforced()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 2);
        await using var factory = CreateFactory(connectionString);
        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var missingMac = await AllocateAsync(
            administrator,
            IdentityRequest(
                "ALLOC-0703-A",
                "SN-ROUTER-0703",
                macAddress: null));
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.07", OperatorPassword);
        var rejected = await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            StartRequest("START-0703", missingMac));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var missingPayload = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "IDENTITY_REQUIRED_IDENTIFIER_MISSING",
            missingPayload.GetProperty("code").GetString());
        Assert.Contains("MacAddress", missingPayload.GetProperty("message").GetString());

        var duplicate = await administrator.PostAsJsonAsync(
            "/api/integration/product-identities",
            IdentityRequest(
                "ALLOC-0703-B",
                "SN-ROUTER-0703",
                "00:11:22:33:44:59"));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "IDENTITY_VALUE_ALREADY_EXISTS",
            (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var mixedAllocation = await administrator.PostAsJsonAsync(
            "/api/integration/product-identities",
            new
            {
                sourceSystem = "IDENTITY-IMPORT",
                idempotencyKey = "ALLOC-0703-MIXED",
                materialCode = "ROUTER-FG-01",
                identifiers = new[]
                {
                    new
                    {
                        type = "SerialNumber",
                        value = "SN-ROUTER-0703-MIXED",
                        sourceType = "Erp",
                        sourceSystem = "ERP-U8",
                        sourceReference = "ERP-SERIAL:MIXED-0703",
                    },
                    new
                    {
                        type = "MacAddress",
                        value = "02:00:00:00:07:03",
                        sourceType = "DemoControlledPool",
                        sourceSystem = "DEMO-POOL",
                        sourceReference = "DEMO-MAC:MIXED-0703",
                    },
                },
            });
        mixedAllocation.EnsureSuccessStatusCode();
        var mixedId = (await mixedAllocation.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("productIdentityId").GetGuid();
        var mixedRejected = await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            StartRequest("START-0703-MIXED", mixedId));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, mixedRejected.StatusCode);
        Assert.Equal(
            "IDENTITY_DEMO_NOT_ALLOWED",
            (await mixedRejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var unregistered = await administrator.PostAsJsonAsync(
            "/api/integration/product-identities",
            new
            {
                sourceSystem = "IDENTITY-IMPORT",
                idempotencyKey = "ALLOC-0703-UNREGISTERED",
                materialCode = "ROUTER-FG-01",
                identifiers = new[]
                {
                    new
                    {
                        type = "SerialNumber",
                        value = "SN-ROUTER-0703-UNREGISTERED",
                        sourceType = "Erp",
                        sourceSystem = "UNREGISTERED-ERP",
                        sourceReference = "UNTRUSTED",
                    },
                },
            });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, unregistered.StatusCode);
        Assert.Equal(
            "IDENTITY_SOURCE_NOT_AUTHORIZED",
            (await unregistered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task IdentityCorrectionsAndLabelActionsRemainAppendOnly()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 1);
        await using var factory = CreateFactory(connectionString);
        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var identityId = await AllocateAsync(
            administrator,
            IdentityRequest("ALLOC-0704", "SN-ROUTER-0704", "00:11:22:33:44:60"));
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.07", OperatorPassword);

        var printed = await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/labels",
            new
            {
                templateVersion = "FG-LABEL-1.0",
                printer = "ZEBRA-LINE-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });
        Assert.Equal(System.Net.HttpStatusCode.Created, printed.StatusCode);
        var labelId = (await printed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("labelId").GetGuid();
        (await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/labels/{labelId}/reprint",
            new { reason = "首张标签污损", occurredAtUtc = DateTimeOffset.UtcNow }))
            .EnsureSuccessStatusCode();
        var replaced = await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/labels/{labelId}/replace",
            new
            {
                templateVersion = "FG-LABEL-1.1",
                printer = "ZEBRA-LINE-01",
                reason = "模板受控升级",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });
        replaced.EnsureSuccessStatusCode();
        var replacementId = (await replaced.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("labelId").GetGuid();
        (await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/labels/{replacementId}/void",
            new { reason = "发现打印内容错误", occurredAtUtc = DateTimeOffset.UtcNow }))
            .EnsureSuccessStatusCode();

        var startRequest = StartRequest("START-0704", identityId);
        (await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            startRequest)).EnsureSuccessStatusCode();
        (await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/unbind",
            new
            {
                reason = "投产前发现工单扫描错误",
                location = "LINE-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            })).EnsureSuccessStatusCode();
        (await station.PostAsJsonAsync(
            $"/api/execution/product-identities/{identityId}/void",
            new
            {
                reason = "ERP 已撤销该序列号",
                location = "LINE-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            })).EnsureSuccessStatusCode();

        var replayAfterCorrection = await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            startRequest);
        replayAfterCorrection.EnsureSuccessStatusCode();
        Assert.True((await replayAfterCorrection.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isReplay").GetBoolean());

        await using var db = CreateContext(connectionString);
        var order = await db.ProductionOrders.SingleAsync(item => item.Id == orderId);
        Assert.Equal(0, order.StartedQuantity);
        Assert.Equal(ProductionOrderStatus.Released, order.Status);
        var eventTypes = await db.ManufacturingEvents
            .Where(item => item.ProductIdentityId == identityId)
            .Select(item => item.EventType)
            .ToArrayAsync();
        Assert.Contains("LABEL_PRINTED", eventTypes);
        Assert.Contains("LABEL_REPRINTED", eventTypes);
        Assert.Contains("LABEL_REPLACED", eventTypes);
        Assert.Contains("LABEL_VOIDED", eventTypes);
        Assert.Contains("IDENTITY_UNBOUND", eventTypes);
        Assert.Contains("START_WIP_REVERSED", eventTypes);
        Assert.Contains("IDENTITY_VOIDED", eventTypes);
        Assert.Equal(ProductLabelStatus.Replaced, (await db.ProductLabels.SingleAsync(
            item => item.Id == labelId)).Status);
        Assert.Equal(ProductLabelStatus.Voided, (await db.ProductLabels.SingleAsync(
            item => item.Id == replacementId)).Status);
        var receiptId = await db.StartWipCommandReceipts
            .Where(item => item.ProductIdentityId == identityId)
            .Select(item => item.Id)
            .SingleAsync();
        var receiptUpdate = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[StartWipCommandReceipts]
                SET [OrderStatus] = N'tampered'
                WHERE [Id] = {{receiptId}};
                """));
        Assert.Equal(51013, receiptUpdate.Number);
    }

    [SqlServerFact]
    public async Task DemoIdentityIsExplicitAndCannotEnterAnErpPolicyOrder()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 1);
        await using var factory = CreateFactory(connectionString);
        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var allocation = await administrator.PostAsJsonAsync(
            "/api/integration/product-identities",
            new
            {
                sourceSystem = "DEMO-IDENTITY-ADAPTER",
                idempotencyKey = "ALLOC-DEMO-0705",
                materialCode = "ROUTER-FG-01",
                identifiers = new[]
                {
                    new
                    {
                        type = "SerialNumber",
                        value = "DEMO-SN-0705",
                        sourceType = "DemoControlledPool",
                        sourceSystem = "DEMO-POOL",
                        sourceReference = "DEMO-LEASE-0705",
                    },
                    new
                    {
                        type = "MacAddress",
                        value = "02:00:00:00:07:05",
                        sourceType = "DemoControlledPool",
                        sourceSystem = "DEMO-POOL",
                        sourceReference = "DEMO-LEASE-0705-MAC",
                    },
                },
            });
        allocation.EnsureSuccessStatusCode();
        var payload = await allocation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("isDemo").GetBoolean());
        Assert.Equal("DemoControlledPool", payload.GetProperty("serialSourceType").GetString());

        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.07", OperatorPassword);
        var rejected = await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            StartRequest("START-DEMO-0705", payload.GetProperty("productIdentityId").GetGuid()));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(
            "IDENTITY_SOURCE_POLICY_MISMATCH",
            (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task FrozenAllocationTimingRejectsAnIdentityObtainedAfterOrderRelease()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(
            connectionString,
            plannedQuantity: 1,
            allocationTiming: "AtOrderRelease");
        await using var factory = CreateFactory(connectionString);
        using var administrator = factory.CreateClient();
        await LoginAsync(administrator, "administrator.07", AdministratorPassword);
        var identityId = await AllocateAsync(
            administrator,
            IdentityRequest("ALLOC-TIMING-0706", "SN-TIMING-0706", "00:11:22:33:44:61"));
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.07", OperatorPassword);
        var rejected = await station.PostAsJsonAsync(
            $"/api/execution/production-orders/{orderId}/start-wip",
            StartRequest("START-TIMING-0706", identityId));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(
            "IDENTITY_ALLOCATION_TIMING_MISMATCH",
            (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task EveryTicket07EndpointRequiresAuthentication()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var orderId = await SeedReleasedOrderAsync(connectionString, plannedQuantity: 1);
        await using var factory = CreateFactory(connectionString);
        using var anonymous = factory.CreateClient();
        var identityId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            "/api/configuration/identity-sources");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            "/api/integration/product-identities");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/production-orders/{orderId}/start-wip");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Get,
            "/api/execution/product-identities/by-serial/UNKNOWN/workstation");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/unbind");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/void");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/labels");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/labels/{labelId}/reprint");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/labels/{labelId}/void");
        await AssertUnauthorizedAsync(
            anonymous,
            HttpMethod.Post,
            $"/api/execution/product-identities/{identityId}/labels/{labelId}/replace");
    }

    private static object IdentityRequest(
        string idempotencyKey,
        string serialNumber,
        string? macAddress = "00:11:22:33:44:55") => new
        {
            sourceSystem = "IDENTITY-IMPORT",
            idempotencyKey,
            materialCode = "ROUTER-FG-01",
            identifiers = new object[]
        {
            new
            {
                type = "SerialNumber",
                value = serialNumber,
                sourceType = "Erp",
                sourceSystem = "ERP-U8",
                sourceReference = $"ERP-SERIAL:{serialNumber}",
            },
        }.Concat(macAddress is null ? [] : new object[]
        {
            new
            {
                type = "MacAddress",
                value = macAddress,
                sourceType = "MesControlledPool",
                sourceSystem = "MAC-POOL-01",
                sourceReference = "POOL-LEASE-0701",
            },
        }).ToArray(),
        };

    private static object StartRequest(string idempotencyKey, Guid identityId) => new
    {
        sourceSystem = "MES-STATION",
        idempotencyKey,
        productIdentityId = identityId,
        location = "LINE-01",
        occurredAtUtc = DateTimeOffset.UtcNow,
    };

    private static async Task<Guid> AllocateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/integration/product-identities", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("productIdentityId").GetGuid();
    }

    private static async Task<Guid> SeedReleasedOrderAsync(
        string connectionString,
        int plannedQuantity,
        string allocationTiming = "BeforeStartWip")
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var administrator = Account(
            "administrator.07",
            "系统管理员 07",
            AdministratorPassword,
            BusinessRole.SystemAdministrator);
        var operatorUser = Account(
            "operator.07",
            "操作工 07",
            OperatorPassword,
            BusinessRole.Operator);
        var planner = Account(
            "planner.07",
            "计划员 07",
            PlannerPassword,
            BusinessRole.Planner);
        var material = new Material
        {
            Id = Guid.NewGuid(),
            Code = "ROUTER-FG-01",
            Name = "工业路由器成品",
            BaseUnit = "EA",
            TraceabilityMode = TraceabilityMode.Serial,
            IsActive = true,
        };
        var template = new ProductExecutionTemplateVersion
        {
            Id = Guid.NewGuid(),
            MaterialId = material.Id,
            Version = "IDENTITY-ROUTE-1.0",
            Applicability = "Ticket 07 SQL Server integration test",
            DefinitionJson = JsonSerializer.Serialize(new ExecutionTemplateDefinition(
                new ProductDefinition(
                    material.Code,
                    material.Name,
                    "PRODUCT-1.0",
                    TraceabilityMode.Serial.ToString()),
                "Ticket 07 SQL Server integration test",
                new BomDefinition("BOM-1.0", []),
                new RouteDefinition("ROUTE-1.0", [
                    new RouteOperationDefinition(10, "START_WIP", "投入生产"),
                    new RouteOperationDefinition(20, "ASSEMBLY_BIND", "装配绑定")]),
                new TraceabilityPolicyDefinition("TRACE-1.0", "Serial"),
                new IdentityPolicyDefinition(
                    "IDENTITY-1.0",
                    "ERP",
                    ["SerialNumber", "MacAddress"],
                    allocationTiming),
                [],
                [],
                new CompletionGateDefinition("GATE-1.0", []))),
            DefinitionHash = new string('a', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = administrator.Id,
        };
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "PO-IDENTITY-0701",
            MaterialId = material.Id,
            PlannedQuantity = plannedQuantity,
            Status = ProductionOrderStatus.Released,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = "PO-IDENTITY-0701",
            SourceVersion = "1",
        };
        var snapshot = new ProductionOrderExecutionSnapshot
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = order.Id,
            SourceTemplateId = template.Id,
            SnapshotVersion = template.Version,
            DefinitionJson = template.DefinitionJson,
            DefinitionHash = template.DefinitionHash,
            DefinitionHashAlgorithm = template.DefinitionHashAlgorithm,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = administrator.Id,
        };
        var erpSource = IdentitySource(
            "ERP-U8",
            IdentitySourceType.Erp,
            administrator.Id,
            administrator.Id,
            ControlledIdentifierType.SerialNumber);
        var macPool = IdentitySource(
            "MAC-POOL-01",
            IdentitySourceType.MesControlledPool,
            administrator.Id,
            administrator.Id,
            ControlledIdentifierType.MacAddress);
        var demoPool = IdentitySource(
            "DEMO-POOL",
            IdentitySourceType.DemoControlledPool,
            administrator.Id,
            administrator.Id,
            ControlledIdentifierType.SerialNumber,
            ControlledIdentifierType.MacAddress);
        context.AddRange(
            administrator,
            operatorUser,
            planner,
            material,
            template,
            order,
            snapshot,
            erpSource,
            macPool,
            demoPool);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static IdentitySourceRegistration IdentitySource(
        string sourceSystem,
        IdentitySourceType sourceType,
        Guid authorizedCallerUserId,
        Guid registeredByUserId,
        params ControlledIdentifierType[] identifierTypes)
    {
        var source = new IdentitySourceRegistration
        {
            Id = Guid.NewGuid(),
            SourceSystem = sourceSystem,
            SourceType = sourceType,
            IsDemo = sourceType == IdentitySourceType.DemoControlledPool,
            IsActive = true,
            AuthorizationEvidence = $"TEST-AUTHORIZATION:{sourceSystem}",
            AuthorizedCallerUserId = authorizedCallerUserId,
            RegisteredByUserId = registeredByUserId,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
        };
        foreach (var identifierType in identifierTypes)
        {
            source.IdentifierGrants.Add(new IdentitySourceIdentifierGrant
            {
                IdentitySourceRegistrationId = source.Id,
                IdentifierType = identifierType,
            });
        }

        return source;
    }

    private static async Task<Guid> AddReleasedOrderAsync(
        string connectionString,
        string orderNumber,
        int plannedQuantity)
    {
        await using var context = CreateContext(connectionString);
        var original = await context.ProductionOrders
            .AsNoTracking()
            .Include(item => item.Material)
            .SingleAsync(item => item.OrderNumber == "PO-IDENTITY-0701");
        var originalSnapshot = await context.ProductionOrderExecutionSnapshots
            .AsNoTracking()
            .SingleAsync(item => item.ProductionOrderId == original.Id);
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            MaterialId = original.MaterialId,
            PlannedQuantity = plannedQuantity,
            Status = ProductionOrderStatus.InProduction,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = orderNumber,
            SourceVersion = "1",
        };
        var snapshot = new ProductionOrderExecutionSnapshot
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = order.Id,
            SourceTemplateId = originalSnapshot.SourceTemplateId,
            SnapshotVersion = originalSnapshot.SnapshotVersion,
            DefinitionJson = originalSnapshot.DefinitionJson,
            DefinitionHash = originalSnapshot.DefinitionHash,
            DefinitionHashAlgorithm = originalSnapshot.DefinitionHashAlgorithm,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = originalSnapshot.CreatedByUserId,
        };
        context.AddRange(order, snapshot);
        await context.SaveChangesAsync();
        return order.Id;
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

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__MesDatabase", connectionString);
        Environment.SetEnvironmentVariable(
            "Security__JwtSigningKey",
            "IntegrationOnlySigningKey_07_AtLeast32Characters");
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

    private static async Task AssertUnauthorizedAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }
}
