using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Identity;
using Mes.Domain.Integration;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Integration;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
/// <summary>验证 ERP Inbox 的原始证据、幂等冲突、并发收敛、授权和跨表事务原子性。</summary>
public sealed class ProductionOrderIngressApiTests(SqlServerFixture server)
{
    private const string PlannerPassword = "IntegrationOnly-Planner-04!";
    private const string OperatorPassword = "IntegrationOnly-Operator-04!";

    [SqlServerFact]
    public async Task PlannerReceivesAnErpOrderReplayAndWorkbenchResultWithoutDuplicateFacts()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        var request = ValidRequest();

        var first = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            request);
        var replay = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            request);

        first.EnsureSuccessStatusCode();
        replay.EnsureSuccessStatusCode();
        var firstJson = await first.Content.ReadAsStringAsync();
        Assert.Equal(firstJson, await replay.Content.ReadAsStringAsync());
        var accepted = JsonDocument.Parse(firstJson).RootElement;
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal("PRODUCTION_ORDER_ACCEPTED", accepted.GetProperty("code").GetString());
        Assert.Equal(
            "生产订单已接收，可由计划员检查后下达。",
            accepted.GetProperty("message").GetString());

        var workbench = await client.GetFromJsonAsync<JsonElement>(
            "/api/planning/production-orders");
        var order = Assert.Single(workbench.GetProperty("orders").EnumerateArray());
        Assert.Equal("PO-ERP-0401", order.GetProperty("orderNumber").GetString());
        Assert.Equal("Received", order.GetProperty("status").GetString());
        Assert.Equal("7", order.GetProperty("sourceVersion").GetString());
        Assert.Equal("Accepted", order.GetProperty("inboundStatus").GetString());
        Assert.Equal(
            "PRODUCTION_ORDER_ACCEPTED",
            order.GetProperty("inboundResultCode").GetString());
        var inboundResult = Assert.Single(
            workbench.GetProperty("inboundResults").EnumerateArray());
        Assert.Equal("ProductionOrderUpsert", inboundResult.GetProperty("messageType").GetString());
        Assert.Equal("Accepted", inboundResult.GetProperty("status").GetString());

        await using var context = CreateContext(connectionString);
        var inbox = await context.IntegrationInboxMessages.SingleAsync();
        Assert.Equal("ProductionOrderUpsert", inbox.MessageType);
        Assert.Equal(
            "{\"sourceSystem\":\"ERP-U8\",\"messageId\":\"MSG-ERP-0401\",\"businessKey\":\"PO-ERP-0401\",\"sourceVersion\":\"7\",\"contractVersion\":\"1.0\",\"orderNumber\":\"PO-ERP-0401\",\"materialCode\":\"ROUTER-FG-01\",\"plannedQuantity\":10}",
            inbox.PayloadJson);
        var payloadJson = Assert.IsType<string>(inbox.PayloadJson);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))),
            inbox.PayloadHash);
        Assert.Equal(1, await context.ProductionOrders.CountAsync());
        Assert.Equal(
            1,
            await context.BusinessAuditRecords.CountAsync(
                audit => audit.Action == "ERP_PRODUCTION_ORDER_INGRESS"
                    && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded));
    }

    [SqlServerFact]
    public async Task SameIdempotencyKeyWithDifferentPayloadIsRejectedWithoutOverwritingOrder()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        var accepted = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest());
        accepted.EnsureSuccessStatusCode();

        var conflict = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(plannedQuantity: 11));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        var result = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", result.GetProperty("status").GetString());
        Assert.Equal("INBOUND_IDEMPOTENCY_CONFLICT", result.GetProperty("code").GetString());
        Assert.Equal(
            "同一来源消息 ID 的载荷与首次请求不一致；请核对 ERP 重发内容并使用新的消息 ID 提交受控变更。",
            result.GetProperty("message").GetString());

        await using var context = CreateContext(connectionString);
        Assert.Equal(1, await context.IntegrationInboxMessages.CountAsync());
        Assert.Equal(1, await context.IntegrationInboxConflicts.CountAsync());
        Assert.Equal(10, await context.ProductionOrders.Select(order => order.PlannedQuantity).SingleAsync());
        Assert.Equal(
            1,
            await context.BusinessAuditRecords.CountAsync(
                audit => audit.Action == "ERP_PRODUCTION_ORDER_INGRESS"
                    && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied));
    }

    [SqlServerFact]
    public async Task UnknownSourceFieldDifferenceIsAnIdempotencyConflictAndOriginalPayloadIsRetained()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        const string original =
            "{\"sourceSystem\":\"ERP-U8\",\"messageId\":\"MSG-RAW-04\",\"businessKey\":\"PO-RAW-04\",\"sourceVersion\":\"7\",\"contractVersion\":\"1.0\",\"orderNumber\":\"PO-RAW-04\",\"materialCode\":\"ROUTER-FG-01\",\"plannedQuantity\":10,\"erpExtension\":\"A\"}";
        const string changed =
            "{\"sourceSystem\":\"ERP-U8\",\"messageId\":\"MSG-RAW-04\",\"businessKey\":\"PO-RAW-04\",\"sourceVersion\":\"7\",\"contractVersion\":\"1.0\",\"orderNumber\":\"PO-RAW-04\",\"materialCode\":\"ROUTER-FG-01\",\"plannedQuantity\":10,\"erpExtension\":\"B\"}";

        var accepted = await client.PostAsync(
            "/api/integration/erp/production-orders",
            new StringContent(original, Encoding.UTF8, "application/json"));
        var conflict = await client.PostAsync(
            "/api/integration/erp/production-orders",
            new StringContent(changed, Encoding.UTF8, "application/json"));

        accepted.EnsureSuccessStatusCode();
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        await using var context = CreateContext(connectionString);
        var inbox = await context.IntegrationInboxMessages.SingleAsync();
        Assert.Equal(original, inbox.PayloadJson);
    }

    [SqlServerFact]
    public async Task LegacyDtoHashReplayReturnsTheStoredResultAfterEvidenceUpgrade()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        var request = new ProductionOrderIngressRequest(
            "ERP-U8",
            "MSG-LEGACY-04",
            "PO-LEGACY-04",
            "7",
            "1.0",
            "PO-LEGACY-04",
            "ROUTER-FG-01",
            10);
        var legacyPayload = JsonSerializer.Serialize(request);
        await using (var context = CreateContext(connectionString))
        {
            context.IntegrationInboxMessages.Add(new IntegrationInboxMessage
            {
                Id = Guid.NewGuid(),
                SourceSystem = request.SourceSystem,
                MessageId = request.MessageId,
                MessageType = "ProductionOrderUpsert",
                BusinessKey = request.BusinessKey,
                SourceVersion = request.SourceVersion,
                ContractVersion = request.ContractVersion,
                PayloadHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(legacyPayload))),
                PayloadHashAlgorithm = "SHA-256-DTO-V1",
                PayloadJson = null,
                Status = IntegrationInboxStatus.Rejected,
                ResultCode = "MATERIAL_NOT_FOUND",
                ResultMessage = "旧版首次拒绝结果",
                HttpStatusCode = 422,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        var replay = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            request);

        Assert.Equal(422, (int)replay.StatusCode);
        var result = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MATERIAL_NOT_FOUND", result.GetProperty("code").GetString());
        Assert.Equal("旧版首次拒绝结果", result.GetProperty("message").GetString());
        await using var verification = CreateContext(connectionString);
        Assert.Single(await verification.IntegrationInboxMessages.ToArrayAsync());
    }

    [SqlServerFact]
    public async Task InvalidContractUnknownMaterialAndInvalidPayloadReturnStableActionableResults()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);

        var unsupportedRequest = ValidRequest(
            messageId: "MSG-UNSUPPORTED-04",
            orderNumber: "PO-UNSUPPORTED-04",
            contractVersion: "2.0");
        var unsupported = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            unsupportedRequest);
        var unsupportedReplay = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            unsupportedRequest);
        var unknownMaterial = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(
                messageId: "MSG-UNKNOWN-MATERIAL-04",
                orderNumber: "PO-UNKNOWN-MATERIAL-04",
                materialCode: "MISSING-MATERIAL"));
        var invalidPayload = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(
                messageId: "MSG-INVALID-PAYLOAD-04",
                orderNumber: "",
                plannedQuantity: 0));
        var invalidEnvelope = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(
                sourceSystem: "",
                messageId: "",
                orderNumber: "PO-MISSING-ENVELOPE-04"));
        var missingPersistedFields = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            new
            {
                sourceSystem = "ERP-U8",
                messageId = "MSG-MISSING-FIELDS-04",
                businessKey = (string?)null,
                sourceVersion = (string?)null,
                contractVersion = "1.0",
                orderNumber = "PO-MISSING-FIELDS-04",
                materialCode = "ROUTER-FG-01",
                plannedQuantity = 10,
            });

        Assert.Equal(422, (int)unsupported.StatusCode);
        Assert.Equal(
            await unsupported.Content.ReadAsStringAsync(),
            await unsupportedReplay.Content.ReadAsStringAsync());
        await AssertResultAsync(
            unsupported,
            "CONTRACT_VERSION_UNSUPPORTED",
            "当前仅支持生产订单入站契约 1.0；请由 ERP 集成负责人转换版本后重试。");
        await AssertResultAsync(
            unknownMaterial,
            "MATERIAL_NOT_FOUND",
            "MES 中不存在或未启用该成品物料；请先由主数据负责人维护物料后重试。");
        await AssertResultAsync(
            invalidPayload,
            "PRODUCTION_ORDER_PAYLOAD_INVALID",
            "生产订单号、业务键、来源版本、物料编码必须填写，且计划数量必须大于 0。");
        Assert.Equal(400, (int)invalidEnvelope.StatusCode);
        var envelopeResult = await invalidEnvelope.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INBOUND_ENVELOPE_INVALID", envelopeResult.GetProperty("code").GetString());
        Assert.Equal(
            "来源系统和消息 ID 必须填写；请由 ERP 集成负责人修正消息信封后重试。",
            envelopeResult.GetProperty("message").GetString());
        await AssertResultAsync(
            missingPersistedFields,
            "PRODUCTION_ORDER_PAYLOAD_INVALID",
            "生产订单号、业务键、来源版本、物料编码必须填写，且计划数量必须大于 0。");

        var workbench = await client.GetFromJsonAsync<JsonElement>(
            "/api/planning/production-orders");
        var rejectedResults = workbench.GetProperty("inboundResults")
            .EnumerateArray()
            .Select(item => item.GetProperty("resultCode").GetString())
            .ToArray();
        Assert.Equal(4, rejectedResults.Length);
        Assert.Contains("CONTRACT_VERSION_UNSUPPORTED", rejectedResults);
        Assert.Contains("MATERIAL_NOT_FOUND", rejectedResults);
        Assert.Contains("PRODUCTION_ORDER_PAYLOAD_INVALID", rejectedResults);

        await using var context = CreateContext(connectionString);
        Assert.Equal(4, await context.IntegrationInboxMessages.CountAsync());
        Assert.Equal(0, await context.ProductionOrders.CountAsync());
        Assert.Equal(
            5,
            await context.BusinessAuditRecords.CountAsync(
                audit => audit.Action == "ERP_PRODUCTION_ORDER_INGRESS"
                    && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied));
    }

    [SqlServerFact]
    public async Task NewMessageForAnExistingBusinessKeyRequiresControlledChange()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        var accepted = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest());
        accepted.EnsureSuccessStatusCode();

        var change = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(
                messageId: "MSG-ERP-0401-CHANGE",
                sourceVersion: "8",
                plannedQuantity: 11));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, change.StatusCode);
        var result = await change.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PRODUCTION_ORDER_CHANGE_REQUIRED", result.GetProperty("code").GetString());
        Assert.Equal(
            "该 ERP 生产订单已进入 MES；请由计划员按受控变更流程处理新版本，现有工单未被覆盖。",
            result.GetProperty("message").GetString());
        await using var context = CreateContext(connectionString);
        Assert.Equal(2, await context.IntegrationInboxMessages.CountAsync());
        Assert.Equal(1, await context.ProductionOrders.CountAsync());
        Assert.Equal(10, await context.ProductionOrders.Select(order => order.PlannedQuantity).SingleAsync());
    }

    [SqlServerFact]
    public async Task SimulatorUsesTheFormalContractAndOperatorCannotIngressOrReadWorkbench()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var plannerClient = factory.CreateClient();
        await LoginAsync(plannerClient, "planner.04", PlannerPassword);
        var request = ValidRequest(
            messageId: "MSG-SIMULATOR-04",
            orderNumber: "PO-SIMULATOR-04",
            sourceSystem: "ERP-SIMULATOR");

        var simulator = await plannerClient.PostAsJsonAsync(
            "/api/simulator/erp/production-orders",
            request);
        var formalReplay = await plannerClient.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            request);

        simulator.EnsureSuccessStatusCode();
        Assert.Equal(
            await simulator.Content.ReadAsStringAsync(),
            await formalReplay.Content.ReadAsStringAsync());

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator.04", OperatorPassword);
        var forbiddenIngress = await operatorClient.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(messageId: "MSG-FORBIDDEN-04", orderNumber: "PO-FORBIDDEN-04"));
        var forbiddenWorkbench = await operatorClient.GetAsync(
            "/api/planning/production-orders");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbiddenIngress.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbiddenWorkbench.StatusCode);
        await using var context = CreateContext(connectionString);
        Assert.Equal(1, await context.IntegrationInboxMessages.CountAsync());
        Assert.Equal(1, await context.ProductionOrders.CountAsync());
        Assert.Contains(
            await context.BusinessAuditRecords.ToArrayAsync(),
            audit => audit.ActorUsername == "operator.04"
                && audit.Capability == BusinessCapability.ProductionOrderManage
                && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied);
    }

    [SqlServerFact]
    public async Task ConcurrentDeliveryOfTheSameMessageCommitsOneInboxAndOneOrder()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);
        var request = ValidRequest(
            messageId: "MSG-CONCURRENT-04",
            orderNumber: "PO-CONCURRENT-04");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync("/api/integration/erp/production-orders", request)));

        Assert.All(responses, response => response.EnsureSuccessStatusCode());
        var payloads = await Task.WhenAll(
            responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Single(payloads.Distinct(StringComparer.Ordinal));
        await using var context = CreateContext(connectionString);
        Assert.Equal(
            1,
            await context.IntegrationInboxMessages.CountAsync(
                message => message.MessageId == "MSG-CONCURRENT-04"));
        Assert.Equal(
            1,
            await context.ProductionOrders.CountAsync(
                order => order.OrderNumber == "PO-CONCURRENT-04"));
        Assert.Equal(
            1,
            await context.BusinessAuditRecords.CountAsync(
                audit => audit.Action == "ERP_PRODUCTION_ORDER_INGRESS"
                    && audit.BusinessObjectId == "PO-CONCURRENT-04"
                    && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded));
    }

    [SqlServerFact]
    // 通过数据库触发器注入保存阶段故障，证明 Inbox、订单和成功审计确实处于同一事务。
    public async Task DatabaseFailureRollsBackInboxOrderAndSuccessAuditTogether()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedPlannerAndMaterialAsync(connectionString);
        await using (var context = CreateContext(connectionString))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [mes].[TR_ProductionOrders_Ticket04RollbackProbe]
                ON [mes].[ProductionOrders]
                AFTER INSERT
                AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [OrderNumber] = N'PO-ROLLBACK-04')
                        THROW 51004, 'Ticket 04 rollback probe.', 1;
                END;
                """);
        }

        await using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();
        await LoginAsync(client, "planner.04", PlannerPassword);

        var response = await client.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            ValidRequest(messageId: "MSG-ROLLBACK-04", orderNumber: "PO-ROLLBACK-04"));

        Assert.Equal(500, (int)response.StatusCode);
        await using var verification = CreateContext(connectionString);
        Assert.False(await verification.IntegrationInboxMessages.AnyAsync(
            message => message.MessageId == "MSG-ROLLBACK-04"));
        Assert.False(await verification.ProductionOrders.AnyAsync(
            order => order.OrderNumber == "PO-ROLLBACK-04"));
        Assert.False(await verification.BusinessAuditRecords.AnyAsync(
            audit => audit.Action == "ERP_PRODUCTION_ORDER_INGRESS"
                && audit.BusinessObjectId == "PO-ROLLBACK-04"
                && audit.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded));
    }

    private static object ValidRequest(
        string messageId = "MSG-ERP-0401",
        string? businessKey = null,
        string contractVersion = "1.0",
        string sourceVersion = "7",
        string orderNumber = "PO-ERP-0401",
        string materialCode = "ROUTER-FG-01",
        int plannedQuantity = 10,
        string sourceSystem = "ERP-U8") => new
        {
            sourceSystem,
            messageId,
            businessKey = businessKey ?? orderNumber,
            sourceVersion,
            contractVersion,
            orderNumber,
            materialCode,
            plannedQuantity,
        };

    private static async Task AssertResultAsync(
        HttpResponseMessage response,
        string code,
        string message)
    {
        Assert.Equal(422, (int)response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", payload.GetProperty("status").GetString());
        Assert.Equal(code, payload.GetProperty("code").GetString());
        Assert.Equal(message, payload.GetProperty("message").GetString());
    }

    private static async Task SeedPlannerAndMaterialAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var planner = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "planner.04",
            DisplayName = "计划员 04",
            IsActive = true,
            PrimaryRole = BusinessRole.Planner,
        };
        planner.PasswordHash = new PasswordHasher<UserAccount>().HashPassword(
            planner,
            PlannerPassword);
        planner.RoleAssignments.Add(new UserRoleAssignment
        {
            UserAccountId = planner.Id,
            Role = BusinessRole.Planner,
            UserAccount = planner,
        });
        var operatorAccount = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "operator.04",
            DisplayName = "操作工 04",
            IsActive = true,
            PrimaryRole = BusinessRole.Operator,
        };
        operatorAccount.PasswordHash = new PasswordHasher<UserAccount>().HashPassword(
            operatorAccount,
            OperatorPassword);
        operatorAccount.RoleAssignments.Add(new UserRoleAssignment
        {
            UserAccountId = operatorAccount.Id,
            Role = BusinessRole.Operator,
            UserAccount = operatorAccount,
        });
        context.UserAccounts.AddRange(planner, operatorAccount);
        context.Materials.Add(new Material
        {
            Id = Guid.NewGuid(),
            Code = "ROUTER-FG-01",
            Name = "工业路由器成品",
            TraceabilityMode = TraceabilityMode.Serial,
            IsActive = true,
        });
        await context.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__MesDatabase",
            connectionString);
        Environment.SetEnvironmentVariable(
            "Security__JwtSigningKey",
            "IntegrationOnlySigningKey_04_AtLeast32Characters");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static async Task LoginAsync(
        HttpClient client,
        string username,
        string password)
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
