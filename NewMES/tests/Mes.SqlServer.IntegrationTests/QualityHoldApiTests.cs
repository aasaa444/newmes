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
/// <summary>验证不合格报告、独立质量保留、岗位边界、工作台可见性和制造流转门禁。</summary>
public sealed class QualityHoldApiTests(SqlServerFixture server)
{
    private const string OperatorPassword = "IntegrationOnly-Operator-11!";
    private const string QualityPassword = "IntegrationOnly-Quality-11!";
    private const string PlannerPassword = "IntegrationOnly-Planner-11!";

    [SqlServerFact]
    public async Task OperatorReportIsIdempotentAndVisibleToQualityOnly()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        using var quality = factory.CreateClient();
        await LoginAsync(station, "operator.11", OperatorPassword);
        await LoginAsync(quality, "quality.11", QualityPassword);

        var first = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-MANUAL-1101"));
        Assert.Equal(201, (int)first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Open", created.GetProperty("status").GetString());
        Assert.Equal("Active", created.GetProperty("holdStatus").GetString());
        Assert.False(created.GetProperty("isReplay").GetBoolean());

        var replay = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-MANUAL-1101"));
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            created.GetProperty("nonconformanceId").GetGuid(),
            replayed.GetProperty("nonconformanceId").GetGuid());
        Assert.True(replayed.GetProperty("isReplay").GetBoolean());

        var conflict = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(
                setup.FinishedSerialNumber,
                "NC-MANUAL-1101",
                phenomenon: "同一业务键下的另一种异常描述"));
        Assert.Equal(409, (int)conflict.StatusCode);
        Assert.Equal("NONCONFORMANCE_IDEMPOTENCY_CONFLICT", await ErrorCodeAsync(conflict));

        var second = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(
                setup.FinishedSerialNumber,
                "NC-MANUAL-1102",
                phenomenon: "同一产品发现另一处外观异常"));
        Assert.Equal(201, (int)second.StatusCode);
        var secondCreated = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            created.GetProperty("qualityHoldId").GetGuid(),
            secondCreated.GetProperty("qualityHoldId").GetGuid());

        var operatorQueue = await station.GetAsync("/api/quality/nonconformances");
        Assert.Equal(403, (int)operatorQueue.StatusCode);
        var qualityReport = await quality.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-QUALITY-1101"));
        Assert.Equal(403, (int)qualityReport.StatusCode);

        var queue = await quality.GetFromJsonAsync<JsonElement>(
            $"/api/quality/nonconformances?finishedSerialNumber={setup.FinishedSerialNumber}");
        var items = queue.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        var item = items.Single(candidate =>
            candidate.GetProperty("nonconformanceId").GetGuid()
                == created.GetProperty("nonconformanceId").GetGuid());
        Assert.Equal("FINAL_INSPECTION", item.GetProperty("detectedOperationCode").GetString());
        Assert.Equal("VISUAL_DAMAGE", item.GetProperty("defectCode").GetString());
        Assert.Equal("EVIDENCE://PHOTO/1101", item.GetProperty("evidenceReference").GetString());

        await using var verification = CreateContext(setup.ConnectionString);
        Assert.Equal(2, await verification.NonconformanceRecords.CountAsync());
        Assert.Equal(1, await verification.QualityHolds.CountAsync());
        Assert.Equal(
            1,
            await verification.ProductionOrders
                .Where(order => order.Id == setup.ProductionOrderId)
                .Select(order => order.OpenQualityHoldQuantity)
                .SingleAsync());

        var firstRecord = await verification.NonconformanceRecords.SingleAsync(record =>
            record.Id == created.GetProperty("nonconformanceId").GetGuid());
        var hold = await verification.QualityHolds.SingleAsync();
        var nonconformanceEvent = await verification.ManufacturingEvents.SingleAsync(item =>
            item.Id == firstRecord.ManufacturingEventId);
        var holdEvent = await verification.ManufacturingEvents.SingleAsync(item =>
            item.Id == hold.ManufacturingEventId);
        Assert.Equal("NONCONFORMANCE_RECORDED", nonconformanceEvent.EventType);
        Assert.Equal("QUALITY_HOLD_STARTED", holdEvent.EventType);
        Assert.Equal(nonconformanceEvent.Id, holdEvent.CausationEventId);
        Assert.Equal(firstRecord.CorrelationId, nonconformanceEvent.CorrelationId);
        Assert.Equal(hold.CorrelationId, holdEvent.CorrelationId);
        Assert.Equal(
            2,
            await verification.BusinessAuditRecords.CountAsync(item =>
                item.Action == "NONCONFORMANCE_RECORD"
                && item.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded));
        Assert.Equal(
            1,
            await verification.BusinessAuditRecords.CountAsync(item =>
                item.Action == "QUALITY_HOLD_START"
                && item.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded));
        Assert.Equal(
            2,
            await verification.BusinessAuditRecords.CountAsync(item =>
                item.Action == "NONCONFORMANCE_REPORT"
                && item.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied));
        Assert.Equal(
            1,
            await verification.BusinessAuditRecords.CountAsync(item =>
                item.Action == "NONCONFORMANCE_QUEUE_READ"
                && item.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied
                && item.ReasonCode == "CAPABILITY_NOT_GRANTED"));
        Assert.All(
            await verification.BusinessAuditRecords
                .Where(item => item.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied)
                .ToArrayAsync(),
            audit => Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId)));
    }

    [SqlServerFact]
    public async Task ActiveHoldBlocksNormalExecutionAndOrderCompletionWithoutPartialFacts()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        using var planner = factory.CreateClient();
        await LoginAsync(station, "operator.11", OperatorPassword);
        await LoginAsync(planner, "planner.11", PlannerPassword);

        var report = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-GATE-1101"));
        Assert.Equal(201, (int)report.StatusCode);

        await using var baseline = CreateContext(setup.ConnectionString);
        var eventCount = await baseline.ManufacturingEvents.CountAsync();

        var assembly = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerialNumber}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "HELD-ASSEMBLY-1101",
                operationCode = "FINAL_INSPECTION",
                materialCode = "UNUSED-BY-HOLD-GUARD",
                componentSerialNumber = (string?)null,
                lotNumber = "LOT-HELD-1101",
                quantity = 1m,
                unit = "EA",
                location = "LINE-01/FINAL-INSPECTION",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });
        await AssertHeldAsync(assembly);

        var firmware = await station.PostAsJsonAsync(
            $"/api/execution/firmware/{setup.FinishedSerialNumber}/executions",
            new
            {
                sourceSystem = "FW-STATION",
                idempotencyKey = "HELD-FIRMWARE-1101",
                requirementCode = "UNUSED-BY-HOLD-GUARD",
                actualVersion = "1.0.0",
                configurationPackage = "CFG-HELD",
                checksumAlgorithm = "SHA-256",
                checksumValue = new string('A', 64),
                toolId = "FW-TOOL-11",
                toolVersion = "1.0",
                result = "Succeeded",
                diagnosticCode = (string?)null,
                diagnosticMessage = (string?)null,
                retryOfExecutionId = (Guid?)null,
                startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                endedAtUtc = DateTimeOffset.UtcNow,
                location = "LINE-01/FINAL-INSPECTION",
            });
        await AssertHeldAsync(firmware);

        var test = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            new
            {
                sourceSystem = "TEST-STATION",
                idempotencyKey = "HELD-TEST-1101",
                specificationCode = "UNUSED-BY-HOLD-GUARD",
                deviceId = "TESTER-11",
                deviceVersion = "1.0",
                fixtureId = "FIXTURE-11",
                fixtureVersion = "1.0",
                rawReportReference = "EVIDENCE://TEST/HELD-1101",
                retryOfTestRunId = (Guid?)null,
                startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                endedAtUtc = DateTimeOffset.UtcNow,
                location = "LINE-01/FINAL-INSPECTION",
                measurements = new[]
                {
                    new
                    {
                        itemCode = "UNUSED_BY_HOLD_GUARD",
                        rawValue = "1",
                        unit = (string?)null,
                    },
                },
            });
        await AssertHeldAsync(test);

        var completion = await planner.PostAsync(
            $"/api/planning/production-orders/{setup.ProductionOrderId}/complete-execution",
            null);
        Assert.Equal(409, (int)completion.StatusCode);
        Assert.Equal("PRODUCTION_ORDER_QUALITY_HOLD_OPEN", await ErrorCodeAsync(completion));

        await using var verification = CreateContext(setup.ConnectionString);
        Assert.Empty(await verification.MaterialTransactions.ToArrayAsync());
        Assert.Empty(await verification.FirmwareConfigurationExecutions.ToArrayAsync());
        Assert.Empty(await verification.TestRuns.ToArrayAsync());
        Assert.Equal(eventCount, await verification.ManufacturingEvents.CountAsync());
        Assert.Equal(
            ProductionOrderStatus.InProduction,
            await verification.ProductionOrders
                .Where(order => order.Id == setup.ProductionOrderId)
                .Select(order => order.Status)
                .SingleAsync());
    }

    [SqlServerFact]
    public async Task QualityFactsAreAppendOnlyAtTheDatabaseBoundary()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.11", OperatorPassword);
        var report = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-APPEND-ONLY-1101"));
        report.EnsureSuccessStatusCode();

        await using var context = CreateContext(setup.ConnectionString);
        var nonconformanceId = await context.NonconformanceRecords.Select(item => item.Id).SingleAsync();
        var holdId = await context.QualityHolds.Select(item => item.Id).SingleAsync();
        var nonconformanceError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[NonconformanceRecords]
                SET [Phenomenon] = N'未经处置直接改写'
                WHERE [Id] = {{nonconformanceId}};
                """));
        var holdError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                DELETE FROM [mes].[QualityHolds]
                WHERE [Id] = {{holdId}};
                """));
        Assert.Equal(51016, nonconformanceError.Number);
        Assert.Equal(51017, holdError.Number);
        Assert.Equal(1, await context.NonconformanceRecords.CountAsync());
        Assert.Equal(1, await context.QualityHolds.CountAsync());
    }

    [SqlServerFact]
    public async Task DatabaseFailureRollsBackTheEntireQualityFactPackage()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using (var context = CreateContext(setup.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [mes].[TR_NonconformanceRecords_ForceRollback]
                ON [mes].[NonconformanceRecords]
                AFTER INSERT
                AS
                BEGIN
                    THROW 51018, 'Injected nonconformance failure.', 1;
                END
                """);
        }

        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.11", OperatorPassword);
        await using var baseline = CreateContext(setup.ConnectionString);
        var auditCount = await baseline.BusinessAuditRecords.CountAsync();
        var response = await station.PostAsJsonAsync(
            "/api/quality/nonconformances",
            ReportRequest(setup.FinishedSerialNumber, "NC-ROLLBACK-1101"));
        Assert.Equal(500, (int)response.StatusCode);

        await using var verification = CreateContext(setup.ConnectionString);
        Assert.False(await verification.NonconformanceRecords.AnyAsync());
        Assert.False(await verification.QualityHolds.AnyAsync());
        Assert.False(await verification.ManufacturingEvents.AnyAsync());
        Assert.Equal(auditCount, await verification.BusinessAuditRecords.CountAsync());
        Assert.Equal(
            0,
            await verification.ProductionOrders
                .Where(order => order.Id == setup.ProductionOrderId)
                .Select(order => order.OpenQualityHoldQuantity)
                .SingleAsync());
    }

    private static object ReportRequest(
        string finishedSerialNumber,
        string idempotencyKey,
        string phenomenon = "外壳表面存在可见划伤") => new
        {
            sourceSystem = "FINAL-INSPECTION-STATION",
            idempotencyKey,
            finishedSerialNumber,
            detectedOperationCode = "FINAL_INSPECTION",
            defectCode = "VISUAL_DAMAGE",
            phenomenon,
            evidenceReference = "EVIDENCE://PHOTO/1101",
            detectedAtUtc = new DateTimeOffset(2026, 7, 30, 6, 0, 0, TimeSpan.Zero),
            location = "LINE-01/FINAL-INSPECTION",
        };

    private static async Task<QualitySetup> CreateSetupAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var operatorAccount = Account(
            "operator.11",
            "Ticket 11 operator",
            OperatorPassword,
            BusinessRole.Operator);
        var material = new Material
        {
            Id = Guid.NewGuid(),
            Code = "ROUTER-FG-11",
            Name = "Industrial router quality fixture",
            BaseUnit = "EA",
            TraceabilityMode = TraceabilityMode.Serial,
            IsActive = true,
        };
        var definition = new ExecutionTemplateDefinition(
            new ProductDefinition(material.Code, material.Name, "PRODUCT-11", "Serial"),
            "Ticket 11 quality fixture",
            new BomDefinition("BOM-11", []),
            new RouteDefinition(
                "ROUTE-11",
                [
                    new RouteOperationDefinition(50, "FINAL_INSPECTION", "Final inspection"),
                    new RouteOperationDefinition(60, "COMPLETE", "Complete"),
                ]),
            new TraceabilityPolicyDefinition("TRACE-11", "Serial"),
            new IdentityPolicyDefinition("IDENTITY-11", "ERP", ["SerialNumber"]),
            [],
            [],
            new CompletionGateDefinition("GATE-11", ["QUALITY_RELEASED"]));
        var definitionJson = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);
        var template = new ProductExecutionTemplateVersion
        {
            Id = Guid.NewGuid(),
            MaterialId = material.Id,
            Version = "TEMPLATE-11",
            Applicability = "Ticket 11 quality fixture",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('A', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = operatorAccount.Id,
        };
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "PO-QUALITY-1101",
            MaterialId = material.Id,
            PlannedQuantity = 1,
            StartedQuantity = 1,
            QualifiedQuantity = 1,
            Status = ProductionOrderStatus.InProduction,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = "PO-QUALITY-1101",
            SourceVersion = "1",
        };
        var snapshot = new ProductionOrderExecutionSnapshot
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = order.Id,
            SourceTemplateId = template.Id,
            SnapshotVersion = template.Version,
            DefinitionJson = definitionJson,
            DefinitionHash = template.DefinitionHash,
            DefinitionHashAlgorithm = template.DefinitionHashAlgorithm,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = operatorAccount.Id,
        };
        var identity = new ProductIdentity
        {
            Id = Guid.NewGuid(),
            MaterialId = material.Id,
            SerialNumber = "ROUTER-SN-1101",
            SerialSourceType = IdentitySourceType.Erp,
            SerialSourceSystem = "ERP-U8",
            SerialSourceReference = "ERP-SN-1101",
            Status = ProductIdentityStatus.Bound,
            ProductionOrderId = order.Id,
            ExecutionSnapshotId = snapshot.Id,
            NextOperationCode = "FINAL_INSPECTION",
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            BoundAtUtc = DateTimeOffset.UtcNow,
            StartSourceSystem = "MES-STATION",
            StartIdempotencyKey = "START-WIP-1101",
            StartCommandHash = new string('D', 64),
            AllocationSourceSystem = "ERP-U8",
            AllocationIdempotencyKey = "ALLOCATE-1101",
            AllocationCommandHash = new string('C', 64),
        };
        context.AddRange(
            operatorAccount,
            Account("quality.11", "Ticket 11 quality engineer", QualityPassword, BusinessRole.QualityEngineer),
            Account("planner.11", "Ticket 11 planner", PlannerPassword, BusinessRole.Planner),
            material,
            template,
            order,
            snapshot,
            identity);
        await context.SaveChangesAsync();
        return new QualitySetup(connectionString, identity.SerialNumber, order.Id);
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
            "IntegrationOnlySigningKey_11_AtLeast32Characters");
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

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task AssertHeldAsync(HttpResponseMessage response)
    {
        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal("QUALITY_HOLD_ACTIVE", await ErrorCodeAsync(response));
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }

    private sealed record QualitySetup(
        string ConnectionString,
        string FinishedSerialNumber,
        Guid ProductionOrderId);
}
