using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Domain.Execution;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
/// <summary>验证规范职责分离、快照项目判定、失败复测、逐项证据、并发和只追加保护。</summary>
public sealed class TestExecutionApiTests(SqlServerFixture server)
{
    private const string EngineerPassword = "IntegrationOnly-Engineer-10!";
    private const string QualityPassword = "IntegrationOnly-Quality-10!";
    private const string OperatorPassword = "IntegrationOnly-Operator-10!";
    private const string PlannerPassword = "IntegrationOnly-Planner-10!";

    [SqlServerFact]
    public async Task ProcessEngineerCreatesDraftAndQualityEngineerApprovesVersion()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedBaseAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var engineer = factory.CreateClient();
        using var quality = factory.CreateClient();
        using var station = factory.CreateClient();
        await LoginAsync(engineer, "engineer.10", EngineerPassword);
        await LoginAsync(quality, "quality.10", QualityPassword);
        await LoginAsync(station, "operator.10", OperatorPassword);

        var unstoreableLimit = await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest(
                version: "OVERFLOW",
                lowerLimit: 1000000000000m,
                upperLimit: null));
        Assert.Equal(422, (int)unstoreableLimit.StatusCode);
        Assert.Equal("TEST_SPECIFICATION_ITEM_INVALID", await ErrorCodeAsync(unstoreableLimit));

        var created = await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest());
        Assert.Equal(201, (int)created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", draft.GetProperty("status").GetString());

        var engineerRead = await engineer.GetAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0");
        engineerRead.EnsureSuccessStatusCode();
        var stationRead = await station.GetAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0");
        Assert.Equal(403, (int)stationRead.StatusCode);

        var engineerApproval = await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0/approve",
            new { approvalEvidenceReference = "Quality approval QA-10-001" });
        Assert.Equal(403, (int)engineerApproval.StatusCode);

        var qualityCreation = await quality.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest("2.0"));
        Assert.Equal(403, (int)qualityCreation.StatusCode);

        var approved = await quality.PostAsJsonAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0/approve",
            new { approvalEvidenceReference = "Quality approval QA-10-001" });
        Assert.Equal(200, (int)approved.StatusCode);
        var approval = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", approval.GetProperty("status").GetString());
        Assert.Equal("quality.10", approval.GetProperty("approvedByUsername").GetString());

        var read = await quality.GetAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0");
        read.EnsureSuccessStatusCode();
        var specification = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FUNCTION_TEST", specification.GetProperty("operationCode").GetString());
        Assert.Equal(3, specification.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(specification.GetProperty("definitionHash").GetString()));

        await using var context = CreateContext(connectionString);
        var specificationId = specification.GetProperty("specificationId").GetGuid();
        var updateError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[TestSpecificationVersions]
                SET [Applicability] = N'tampered'
                WHERE [Id] = {{specificationId}};
                """));
        var deleteError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                DELETE FROM [mes].[TestSpecificationVersions]
                WHERE [Id] = {{specificationId}};
                """));
        Assert.Equal(51011, updateError.Number);
        Assert.Equal(51011, deleteError.Number);
        var forgedId = Guid.NewGuid();
        var forgedApprovalError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO [mes].[TestSpecificationVersions]
                    ([Id], [Code], [Version], [MaterialId], [OperationCode], [Applicability],
                     [DefinitionJson], [DefinitionHash], [DefinitionHashAlgorithm], [IsApproved],
                     [CreatedAtUtc], [CreatedByUserId], [ApprovedAtUtc], [ApprovedByUserId],
                     [ApprovedByUsername], [ApprovalEvidenceReference])
                SELECT
                    {{forgedId}}, N'FORGED-APPROVAL', N'1.0', [MaterialId], [OperationCode],
                    [Applicability], [DefinitionJson], [DefinitionHash], [DefinitionHashAlgorithm],
                    1, [CreatedAtUtc], [CreatedByUserId], SYSDATETIMEOFFSET(), [ApprovedByUserId],
                    N'forged.user', N'forged evidence'
                FROM [mes].[TestSpecificationVersions]
                WHERE [Id] = {{specificationId}};
                """));
        Assert.Equal(51012, forgedApprovalError.Number);

        var secondDraftResponse = await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest("2.0"));
        secondDraftResponse.EnsureSuccessStatusCode();
        var secondDraft = await secondDraftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(
            specification.GetProperty("definitionHash").GetString(),
            secondDraft.GetProperty("definitionHash").GetString());
    }

    [SqlServerFact]
    public async Task OnlyApprovedApplicableSpecificationIsFrozenIntoOrderSnapshot()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await SeedBaseAsync(connectionString);
        await using var factory = CreateFactory(connectionString);
        using var engineer = factory.CreateClient();
        using var quality = factory.CreateClient();
        using var planner = factory.CreateClient();
        await LoginAsync(engineer, "engineer.10", EngineerPassword);
        await LoginAsync(quality, "quality.10", QualityPassword);
        await LoginAsync(planner, "planner.10", PlannerPassword);

        (await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest())).EnsureSuccessStatusCode();
        var draftTemplate = await engineer.PostAsJsonAsync(
            "/api/process/execution-templates",
            TemplateRequest());
        Assert.Equal(422, (int)draftTemplate.StatusCode);
        Assert.Equal(
            "TEST_SPECIFICATION_NOT_APPROVED",
            (await draftTemplate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        (await engineer.PostAsJsonAsync(
            "/api/quality/test-specifications/versions",
            SpecificationRequest(
                code: "PCBA-FUNCTIONAL",
                materialCode: "ROUTER-PCBA-01"))).EnsureSuccessStatusCode();
        (await quality.PostAsJsonAsync(
            "/api/quality/test-specifications/PCBA-FUNCTIONAL/versions/1.0/approve",
            new { approvalEvidenceReference = "Quality approval QA-10-PCBA" })).EnsureSuccessStatusCode();
        var inapplicableTemplate = await engineer.PostAsJsonAsync(
            "/api/process/execution-templates",
            TemplateRequest("PCBA-FUNCTIONAL"));
        Assert.Equal(422, (int)inapplicableTemplate.StatusCode);
        Assert.Equal(
            "TEST_SPECIFICATION_NOT_APPLICABLE",
            (await inapplicableTemplate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        (await quality.PostAsJsonAsync(
            "/api/quality/test-specifications/ROUTER-FUNCTIONAL/versions/1.0/approve",
            new { approvalEvidenceReference = "Quality approval QA-10-002" })).EnsureSuccessStatusCode();
        (await engineer.PostAsJsonAsync(
            "/api/process/execution-templates",
            TemplateRequest())).EnsureSuccessStatusCode();

        var ingress = await planner.PostAsJsonAsync(
            "/api/integration/erp/production-orders",
            new
            {
                sourceSystem = "ERP-U8",
                messageId = "MSG-PO-TEST-1001",
                businessKey = "PO-TEST-1001",
                sourceVersion = "1",
                contractVersion = "1.0",
                orderNumber = "PO-TEST-1001",
                materialCode = "ROUTER-FG-01",
                plannedQuantity = 1,
            });
        ingress.EnsureSuccessStatusCode();
        var orderId = (await ingress.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("productionOrderId").GetGuid();
        (await planner.PostAsync($"/api/planning/production-orders/{orderId}/release", null))
            .EnsureSuccessStatusCode();

        var snapshot = await planner.GetFromJsonAsync<JsonElement>(
            $"/api/planning/production-orders/{orderId}/execution-snapshot");
        var frozen = Assert.Single(snapshot.GetProperty("testSpecifications").EnumerateArray());
        Assert.Equal("FUNCTION_TEST", frozen.GetProperty("operationCode").GetString());
        Assert.Equal("ROUTER-FG-01", frozen.GetProperty("materialCode").GetString());
        Assert.Equal(3, frozen.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(frozen.GetProperty("definitionHash").GetString()));
    }

    [SqlServerFact]
    public async Task SuccessfulRunUsesFrozenItemsPersistsMeasurementsAndAdvancesRoute()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);

        var response = await ExecuteRunAsync(
            station,
            setup.FinishedSerialNumber,
            "TEST-RUN-1001",
            numericValue: "11.40");

        Assert.Equal(201, (int)response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", result.GetProperty("result").GetString());
        Assert.True(result.GetProperty("operationCompleted").GetBoolean());
        Assert.Equal("FINAL_INSPECTION", result.GetProperty("nextOperationCode").GetString());
        Assert.Equal("REPORT://TEST-RUN-1001", result.GetProperty("rawReportReference").GetString());
        Assert.Equal(3, result.GetProperty("measurements").GetArrayLength());
        Assert.All(
            result.GetProperty("measurements").EnumerateArray(),
            measurement => Assert.Equal("Passed", measurement.GetProperty("result").GetString()));

        var workstation = await station.GetFromJsonAsync<JsonElement>(
            $"/api/execution/tests/{setup.FinishedSerialNumber}");
        var specification = Assert.Single(workstation.GetProperty("specifications").EnumerateArray());
        Assert.Equal(11.40m, specification.GetProperty("items")[0].GetProperty("lowerLimit").GetDecimal());
        Assert.Equal("Succeeded", specification.GetProperty("status").GetString());

        var genealogy = await station.GetFromJsonAsync<JsonElement>(
            $"/api/genealogy/products/{setup.FinishedSerialNumber}/tests");
        var run = Assert.Single(genealogy.GetProperty("runs").EnumerateArray());
        Assert.Equal("operator.10", run.GetProperty("actorUsername").GetString());
        Assert.Equal("TESTER-01", run.GetProperty("deviceId").GetString());
        Assert.Equal("FIXTURE-ROUTER-A", run.GetProperty("fixtureId").GetString());
        Assert.Equal("LINE-01/TEST-01", run.GetProperty("location").GetString());
        Assert.NotEqual(Guid.Empty, run.GetProperty("manufacturingEventId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(run.GetProperty("correlationId").GetString()));
    }

    [SqlServerFact]
    public async Task RequiredItemsUnitAndPrecisionAreValidatedAgainstFrozenSpecification()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);

        var wrongUnit = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            RunRequest(
                "TEST-RUN-UNIT",
                Measurements("12.00", numericUnit: "mV")));
        Assert.Equal(422, (int)wrongUnit.StatusCode);
        Assert.Equal("TEST_MEASUREMENT_UNIT_MISMATCH", await ErrorCodeAsync(wrongUnit));

        var excessPrecision = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            RunRequest(
                "TEST-RUN-PRECISION",
                Measurements("11.401")));
        Assert.Equal(422, (int)excessPrecision.StatusCode);
        Assert.Equal("TEST_MEASUREMENT_PRECISION_EXCEEDED", await ErrorCodeAsync(excessPrecision));

        var outOfStorageRange = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            RunRequest(
                "TEST-RUN-STORAGE-RANGE",
                Measurements("1000000000000.00")));
        Assert.Equal(422, (int)outOfStorageRange.StatusCode);
        Assert.Equal(
            "TEST_MEASUREMENT_VALUE_OUT_OF_STORAGE_RANGE",
            await ErrorCodeAsync(outOfStorageRange));

        var missingDevice = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            RunRequest("TEST-RUN-NO-DEVICE", Measurements("12.00"), deviceId: ""));
        Assert.Equal(422, (int)missingDevice.StatusCode);
        Assert.Equal("TEST_RUN_INVALID", await ErrorCodeAsync(missingDevice));

        var missing = await station.PostAsJsonAsync(
            $"/api/execution/tests/{setup.FinishedSerialNumber}/runs",
            RunRequest(
                "TEST-RUN-MISSING",
                [
                    new { itemCode = "DC_INPUT_VOLTAGE", rawValue = "12.00", unit = "V" },
                    new { itemCode = "BOOT_STATUS", rawValue = "READY", unit = (string?)null },
                ]));
        Assert.Equal(201, (int)missing.StatusCode);
        var failed = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", failed.GetProperty("result").GetString());
        Assert.Contains(
            failed.GetProperty("measurements").EnumerateArray(),
            measurement => measurement.GetProperty("itemCode").GetString() == "ETHERNET_LINK"
                && measurement.GetProperty("diagnosticCode").GetString() == "TEST_MEASUREMENT_REQUIRED");

        await using var context = CreateContext(setup.ConnectionString);
        Assert.Equal(1, await context.TestRuns.CountAsync());
        Assert.Equal(
            "FUNCTION_TEST",
            await context.ProductIdentities.Select(item => item.NextOperationCode).SingleAsync());
    }

    [SqlServerFact]
    public async Task FailedRunIsRetainedAndSuccessfulRetryReferencesLatestFailure()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);

        var failedResponse = await ExecuteRunAsync(
            station,
            setup.FinishedSerialNumber,
            "TEST-RUN-FAILED",
            numericValue: "11.39");
        Assert.Equal(201, (int)failedResponse.StatusCode);
        var failed = await failedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", failed.GetProperty("result").GetString());
        Assert.False(failed.GetProperty("operationCompleted").GetBoolean());
        var failedRunId = failed.GetProperty("testRunId").GetGuid();

        var unlinkedRetry = await ExecuteRunAsync(
            station,
            setup.FinishedSerialNumber,
            "TEST-RUN-UNLINKED");
        Assert.Equal(409, (int)unlinkedRetry.StatusCode);
        Assert.Equal("TEST_RETRY_REFERENCE_REQUIRED", await ErrorCodeAsync(unlinkedRetry));

        var retryResponse = await ExecuteRunAsync(
            station,
            setup.FinishedSerialNumber,
            "TEST-RUN-RETRY",
            retryOfTestRunId: failedRunId);
        Assert.Equal(201, (int)retryResponse.StatusCode);
        var retry = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", retry.GetProperty("result").GetString());
        Assert.Equal(failedRunId, retry.GetProperty("retryOfTestRunId").GetGuid());

        var genealogy = await station.GetFromJsonAsync<JsonElement>(
            $"/api/genealogy/products/{setup.FinishedSerialNumber}/tests");
        Assert.Equal(2, genealogy.GetProperty("runs").GetArrayLength());
        Assert.Contains(
            genealogy.GetProperty("runs").EnumerateArray(),
            run => run.GetProperty("testRunId").GetGuid() == failedRunId
                && run.GetProperty("result").GetString() == "Failed");
    }

    [SqlServerFact]
    public async Task IdenticalCommandReplaysAndChangedCommandConflicts()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);

        var first = await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-IDEMPOTENT");
        var replay = await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-IDEMPOTENT");
        var conflict = await ExecuteRunAsync(
            station,
            setup.FinishedSerialNumber,
            "TEST-RUN-IDEMPOTENT",
            numericValue: "12.01");

        Assert.Equal(201, (int)first.StatusCode);
        Assert.Equal(200, (int)replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isReplay").GetBoolean());
        Assert.Equal(409, (int)conflict.StatusCode);
        Assert.Equal("TEST_RUN_IDEMPOTENCY_CONFLICT", await ErrorCodeAsync(conflict));
        await using var context = CreateContext(setup.ConnectionString);
        Assert.Equal(1, await context.TestRuns.CountAsync());
    }

    [SqlServerFact]
    public async Task PausedOrderWrongOperationAndUnauthorizedRoleCannotRecordRuns()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        using var planner = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);
        await LoginAsync(planner, "planner.10", PlannerPassword);
        await using var context = CreateContext(setup.ConnectionString);

        var order = await context.ProductionOrders.SingleAsync();
        order.Status = ProductionOrderStatus.Paused;
        await context.SaveChangesAsync();
        var paused = await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-PAUSED");
        Assert.Equal(409, (int)paused.StatusCode);
        Assert.Equal("TEST_ORDER_STATUS_BLOCKED", await ErrorCodeAsync(paused));

        context.ChangeTracker.Clear();
        order = await context.ProductionOrders.SingleAsync();
        order.Status = ProductionOrderStatus.InProduction;
        var identity = await context.ProductIdentities.SingleAsync();
        identity.NextOperationCode = "FINAL_INSPECTION";
        await context.SaveChangesAsync();
        var wrongOperation = await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-WRONG-OP");
        Assert.Equal(409, (int)wrongOperation.StatusCode);
        Assert.Equal("TEST_OPERATION_MISMATCH", await ErrorCodeAsync(wrongOperation));

        var unauthorized = await ExecuteRunAsync(planner, setup.FinishedSerialNumber, "TEST-RUN-PLANNER");
        Assert.Equal(403, (int)unauthorized.StatusCode);
        Assert.False(await context.TestRuns.AnyAsync());
    }

    [SqlServerFact]
    // 在事务末端注入失败，证明测试运行、测量、事件和路线状态构成不可拆分的事实包。
    public async Task DatabaseFailureRollsBackRunMeasurementsEventsAndRouteAdvance()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using (var context = CreateContext(setup.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [mes].[TR_TestMeasurements_ForceRollback]
                ON [mes].[TestMeasurements]
                AFTER INSERT
                AS
                BEGIN
                    THROW 51010, 'Injected test measurement failure.', 1;
                END
                """);
        }

        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);
        var response = await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-ROLLBACK");
        Assert.Equal(500, (int)response.StatusCode);

        await using var verification = CreateContext(setup.ConnectionString);
        Assert.False(await verification.TestRuns.AnyAsync());
        Assert.False(await verification.TestMeasurements.AnyAsync());
        Assert.False(await verification.ManufacturingEvents.AnyAsync());
        Assert.Equal(
            "FUNCTION_TEST",
            await verification.ProductIdentities.Select(item => item.NextOperationCode).SingleAsync());
    }

    [SqlServerFact]
    public async Task ConfirmedRunsAndMeasurementsAreAppendOnly()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.10", OperatorPassword);
        (await ExecuteRunAsync(station, setup.FinishedSerialNumber, "TEST-RUN-APPEND-ONLY"))
            .EnsureSuccessStatusCode();

        await using var context = CreateContext(setup.ConnectionString);
        var runId = await context.TestRuns.Select(item => item.Id).SingleAsync();
        var measurementId = await context.TestMeasurements.Select(item => item.Id).FirstAsync();
        var runError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[TestRuns] SET [ActorUsername] = N'tampered' WHERE [Id] = {{runId}};
                """));
        var measurementError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                DELETE FROM [mes].[TestMeasurements] WHERE [Id] = {{measurementId}};
                """));

        Assert.Equal(51000, runError.Number);
        Assert.Equal(51000, measurementError.Number);
        Assert.Equal("operator.10", await context.TestRuns.Select(item => item.ActorUsername).SingleAsync());
        Assert.Equal(3, await context.TestMeasurements.CountAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentPassingRunsProduceExactlyOneSuccessFact()
    {
        var setup = await CreateExecutionSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var firstStation = factory.CreateClient();
        using var secondStation = factory.CreateClient();
        await LoginAsync(firstStation, "operator.10", OperatorPassword);
        await LoginAsync(secondStation, "operator.10", OperatorPassword);

        var responses = await Task.WhenAll(
            ExecuteRunAsync(firstStation, setup.FinishedSerialNumber, "TEST-RUN-CONCURRENT-1"),
            ExecuteRunAsync(secondStation, setup.FinishedSerialNumber, "TEST-RUN-CONCURRENT-2"));

        Assert.Single(responses, response => (int)response.StatusCode == 201);
        Assert.Single(responses, response => (int)response.StatusCode == 409);
        await using var context = CreateContext(setup.ConnectionString);
        Assert.Equal(
            1,
            await context.TestRuns.CountAsync(run => run.Result == TestRunResult.Succeeded));
    }

    private static Task<HttpResponseMessage> ExecuteRunAsync(
        HttpClient station,
        string finishedSerialNumber,
        string idempotencyKey,
        string numericValue = "12.00",
        Guid? retryOfTestRunId = null) => station.PostAsJsonAsync(
        $"/api/execution/tests/{finishedSerialNumber}/runs",
        RunRequest(idempotencyKey, Measurements(numericValue), retryOfTestRunId));

    private static object RunRequest(
        string idempotencyKey,
        object[] measurements,
        Guid? retryOfTestRunId = null,
        string deviceId = "TESTER-01") => new
        {
            sourceSystem = "TEST-BENCH",
            idempotencyKey,
            specificationCode = "ROUTER-FUNCTIONAL",
            deviceId,
            deviceVersion = "3.4.0",
            fixtureId = "FIXTURE-ROUTER-A",
            fixtureVersion = "2.1",
            rawReportReference = $"REPORT://{idempotencyKey}",
            retryOfTestRunId,
            startedAtUtc = new DateTimeOffset(2026, 7, 30, 4, 0, 0, TimeSpan.Zero),
            endedAtUtc = new DateTimeOffset(2026, 7, 30, 4, 1, 0, TimeSpan.Zero),
            location = "LINE-01/TEST-01",
            measurements,
        };

    private static object[] Measurements(string numericValue, string numericUnit = "V") =>
    [
        new { itemCode = "DC_INPUT_VOLTAGE", rawValue = numericValue, unit = numericUnit },
        new { itemCode = "BOOT_STATUS", rawValue = "READY", unit = (string?)null },
        new { itemCode = "ETHERNET_LINK", rawValue = "true", unit = (string?)null },
    ];

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static object SpecificationRequest(
        string version = "1.0",
        string code = "ROUTER-FUNCTIONAL",
        string materialCode = "ROUTER-FG-01",
        string operationCode = "FUNCTION_TEST",
        decimal? lowerLimit = 11.40m,
        decimal? upperLimit = 12.60m) => new
        {
            code,
            version,
            materialCode,
            operationCode,
            applicability = "Industrial router final assembly pilot candidate",
            items = new object[]
        {
            new
            {
                code = "DC_INPUT_VOLTAGE",
                name = "DC input voltage",
                dataType = "Numeric",
                required = true,
                unit = "V",
                decimalPlaces = 2,
                lowerLimit,
                upperLimit,
            },
            new
            {
                code = "BOOT_STATUS",
                name = "Boot status",
                dataType = "Text",
                required = true,
                expectedText = "READY",
            },
            new
            {
                code = "ETHERNET_LINK",
                name = "Ethernet link",
                dataType = "Boolean",
                required = true,
                expectedBoolean = true,
            },
        },
        };

    private static object TemplateRequest(string specificationCode = "ROUTER-FUNCTIONAL") => new
    {
        materialCode = "ROUTER-FG-01",
        productVersion = "PRODUCT-1.0",
        version = "TEMPLATE-10-1.0",
        applicability = "Ticket 10 industrial router pilot candidate",
        bom = new
        {
            version = "BOM-10-1.0",
            components = new[]
            {
                new
                {
                    materialCode = "ROUTER-PCBA-01",
                    quantityPer = 1m,
                    unit = "EA",
                    traceabilityMode = "Serial",
                    assemblyOperationCode = "ASSEMBLY_BIND",
                    consumptionRule = "PerProductActual",
                },
            },
        },
        route = new
        {
            version = "ROUTE-10-1.0",
            operations = new[]
            {
                new { sequence = 10, code = "START_WIP", name = "Start WIP" },
                new { sequence = 20, code = "ASSEMBLY_BIND", name = "Assembly binding" },
                new { sequence = 30, code = "FW_CONFIG", name = "Firmware configuration" },
                new { sequence = 40, code = "FUNCTION_TEST", name = "Function test" },
                new { sequence = 50, code = "FINAL_INSPECTION", name = "Final inspection" },
                new { sequence = 60, code = "COMPLETE", name = "Complete" },
            },
        },
        traceabilityPolicy = new { version = "TRACE-10-1.0", finishedProductMode = "Serial" },
        identityPolicy = new
        {
            version = "IDENTITY-10-1.0",
            finishedSerialSource = "ERP",
            requiredIdentifiers = new[] { "SerialNumber" },
            allocationTiming = "BeforeStartWip",
        },
        firmwareRequirements = new[]
        {
            new
            {
                code = "ROUTER-OS",
                version = "R1.4.7",
                required = true,
                evidenceReference = "Approved firmware baseline 10",
                operationCode = "FW_CONFIG",
                configurationPackage = "CFG-FACTORY-A-3",
                checksumAlgorithm = "SHA-256",
                expectedChecksum = "A1B2C3D4",
            },
        },
        testSpecifications = new[]
        {
            new
            {
                code = specificationCode,
                version = "1.0",
                required = true,
                evidenceReference = "Quality approval QA-10-002",
            },
        },
        completionGate = new
        {
            version = "GATE-10-1.0",
            requirements = new[] { "FIRMWARE_COMPLETE", "TESTS_PASSED" },
        },
    };

    private static async Task SeedBaseAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        context.AddRange(
            Account("engineer.10", "Ticket 10 process engineer", EngineerPassword, BusinessRole.ProcessEngineer),
            Account("quality.10", "Ticket 10 quality engineer", QualityPassword, BusinessRole.QualityEngineer),
            Account("operator.10", "Ticket 10 test operator", OperatorPassword, BusinessRole.Operator),
            Account("planner.10", "Ticket 10 planner", PlannerPassword, BusinessRole.Planner),
            new Material
            {
                Id = Guid.NewGuid(),
                Code = "ROUTER-FG-01",
                Name = "Industrial router",
                BaseUnit = "EA",
                TraceabilityMode = TraceabilityMode.Serial,
                IsActive = true,
            },
            new Material
            {
                Id = Guid.NewGuid(),
                Code = "ROUTER-PCBA-01",
                Name = "Router PCBA",
                BaseUnit = "EA",
                TraceabilityMode = TraceabilityMode.Serial,
                IsActive = true,
            });
        await context.SaveChangesAsync();
    }

    private static async Task<ExecutionSetup> CreateExecutionSetupAsync(string connectionString)
    {
        await SeedBaseAsync(connectionString);
        await using var context = CreateContext(connectionString);
        var material = await context.Materials.SingleAsync(item => item.Code == "ROUTER-FG-01");
        var actor = await context.UserAccounts.SingleAsync(item => item.Username == "operator.10");
        var items = new TestSpecificationItemDefinition[]
        {
            new("DC_INPUT_VOLTAGE", "DC input voltage", "Numeric", true, "V", 2, 11.40m, 12.60m),
            new("BOOT_STATUS", "Boot status", "Text", true, ExpectedText: "READY"),
            new("ETHERNET_LINK", "Ethernet link", "Boolean", true, ExpectedBoolean: true),
        };
        var definition = new ExecutionTemplateDefinition(
            new ProductDefinition(material.Code, material.Name, "PRODUCT-1.0", "Serial"),
            "Ticket 10 execution fixture",
            new BomDefinition("BOM-1.0", []),
            new RouteDefinition(
                "ROUTE-1.0",
                [
                    new RouteOperationDefinition(40, "FUNCTION_TEST", "Function test"),
                    new RouteOperationDefinition(50, "FINAL_INSPECTION", "Final inspection"),
                    new RouteOperationDefinition(60, "COMPLETE", "Complete"),
                ]),
            new TraceabilityPolicyDefinition("TRACE-1.0", "Serial"),
            new IdentityPolicyDefinition("IDENTITY-1.0", "ERP", ["SerialNumber"]),
            [],
            [
                new TestSpecificationReferenceDefinition(
                    "ROUTER-FUNCTIONAL",
                    "1.0",
                    true,
                    "Quality approval QA-10-EXECUTION",
                    "FUNCTION_TEST",
                    material.Code,
                    new string('A', 64),
                    "SHA-256-JSON-V1",
                    items),
            ],
            new CompletionGateDefinition("GATE-1.0", ["TESTS_PASSED"]));
        var definitionJson = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);
        var template = new ProductExecutionTemplateVersion
        {
            Id = Guid.NewGuid(),
            MaterialId = material.Id,
            Version = "TEMPLATE-10-EXECUTION",
            Applicability = "Ticket 10 execution fixture",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('B', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = actor.Id,
        };
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "PO-TEST-EXECUTION-1001",
            MaterialId = material.Id,
            PlannedQuantity = 1,
            StartedQuantity = 1,
            Status = ProductionOrderStatus.InProduction,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = "PO-TEST-EXECUTION-1001",
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
            CreatedByUserId = actor.Id,
        };
        var identity = new ProductIdentity
        {
            Id = Guid.NewGuid(),
            MaterialId = material.Id,
            SerialNumber = "ROUTER-SN-1001",
            SerialSourceType = IdentitySourceType.Erp,
            SerialSourceSystem = "ERP-U8",
            SerialSourceReference = "ERP-SN-1001",
            Status = ProductIdentityStatus.Bound,
            ProductionOrderId = order.Id,
            ExecutionSnapshotId = snapshot.Id,
            NextOperationCode = "FUNCTION_TEST",
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            BoundAtUtc = DateTimeOffset.UtcNow,
            StartSourceSystem = "MES-STATION",
            StartIdempotencyKey = "START-WIP-1001",
            StartCommandHash = new string('D', 64),
            AllocationSourceSystem = "ERP-U8",
            AllocationIdempotencyKey = "ALLOCATE-1001",
            AllocationCommandHash = new string('C', 64),
        };
        context.AddRange(template, order, snapshot, identity);
        await context.SaveChangesAsync();
        return new ExecutionSetup(connectionString, identity.SerialNumber);
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
            "IntegrationOnlySigningKey_10_AtLeast32Characters");
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

    private sealed record ExecutionSetup(string ConnectionString, string FinishedSerialNumber);
}
