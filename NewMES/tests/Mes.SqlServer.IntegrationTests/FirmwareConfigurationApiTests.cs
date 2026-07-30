using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class FirmwareConfigurationApiTests(SqlServerFixture server)
{
    private const string OperatorPassword = "IntegrationOnly-Operator-09!";
    private const string QualityPassword = "IntegrationOnly-Quality-09!";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task SuccessfulExecutionUsesFrozenRequirementAdvancesRouteAndAppearsInGenealogy()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var response = await station.PostAsJsonAsync(
            $"/api/execution/firmware/{setup.FinishedSerial}/executions",
            new
            {
                sourceSystem = "FW-STATION",
                idempotencyKey = "FW-EXECUTE-0901",
                requirementCode = "ROUTER-OS",
                actualVersion = "R1.4.7",
                configurationPackage = "CFG-FACTORY-A-3",
                checksumAlgorithm = "SHA-256",
                checksumValue = "A1B2C3D4",
                toolId = "FLASHER-01",
                toolVersion = "5.2.0",
                result = "Succeeded",
                startedAtUtc = new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero),
                endedAtUtc = new DateTimeOffset(2026, 7, 30, 3, 2, 0, TimeSpan.Zero),
                location = "LINE-01/FW-01",
            });

        Assert.Equal(201, (int)response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", result.GetProperty("result").GetString());
        Assert.True(result.GetProperty("operationCompleted").GetBoolean());
        Assert.Equal("FUNCTION_TEST", result.GetProperty("nextOperationCode").GetString());

        var workstationResponse = await station.GetAsync(
            $"/api/execution/firmware/{setup.FinishedSerial}");
        workstationResponse.EnsureSuccessStatusCode();
        var workstation = await workstationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requirement = Assert.Single(workstation.GetProperty("requirements").EnumerateArray());
        Assert.Equal("R1.4.7", requirement.GetProperty("requiredVersion").GetString());
        Assert.Equal("CFG-FACTORY-A-3", requirement.GetProperty("requiredConfigurationPackage").GetString());
        Assert.Equal("Succeeded", requirement.GetProperty("status").GetString());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/firmware");
        genealogyResponse.EnsureSuccessStatusCode();
        var genealogy = await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var execution = Assert.Single(genealogy.GetProperty("executions").EnumerateArray());
        Assert.Equal("R1.4.7", execution.GetProperty("requiredVersion").GetString());
        Assert.Equal("R1.4.7", execution.GetProperty("actualVersion").GetString());
        Assert.Equal("A1B2C3D4", execution.GetProperty("actualChecksum").GetString());
        Assert.Equal("FLASHER-01", execution.GetProperty("toolId").GetString());
        Assert.Equal("Succeeded", execution.GetProperty("result").GetString());
        Assert.Equal("FW_CONFIG", execution.GetProperty("operationCode").GetString());
        Assert.Equal("operator.09", execution.GetProperty("actorUsername").GetString());
        Assert.Equal("LINE-01/FW-01", execution.GetProperty("location").GetString());
        Assert.NotEqual(Guid.Empty, execution.GetProperty("productionOrderId").GetGuid());
        Assert.NotEqual(Guid.Empty, execution.GetProperty("executionSnapshotId").GetGuid());
        Assert.NotEqual(Guid.Empty, execution.GetProperty("manufacturingEventId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(execution.GetProperty("correlationId").GetString()));
        Assert.NotEqual(default, execution.GetProperty("recordedAtUtc").GetDateTimeOffset());
    }

    [SqlServerFact]
    public async Task ChecksumFailureIsPreservedAndSuccessfulRetryMustReferenceIt()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var failed = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-FAIL-0901",
            checksum: "WRONG-CHECKSUM");
        Assert.Equal(201, (int)failed.StatusCode);
        var failedResult = await failed.Content.ReadFromJsonAsync<JsonElement>();
        var failedExecutionId = failedResult.GetProperty("executionId").GetGuid();
        Assert.Equal("Failed", failedResult.GetProperty("result").GetString());
        Assert.Equal("FIRMWARE_EVIDENCE_MISMATCH", failedResult.GetProperty("diagnosticCode").GetString());
        Assert.False(failedResult.GetProperty("operationCompleted").GetBoolean());
        Assert.Equal("FW_CONFIG", failedResult.GetProperty("nextOperationCode").GetString());

        var missingReference = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-RETRY-NO-REF-0901");
        Assert.Equal(409, (int)missingReference.StatusCode);
        Assert.Equal(
            "FIRMWARE_RETRY_REFERENCE_REQUIRED",
            (await missingReference.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var retried = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-RETRY-0901",
            retryOfExecutionId: failedExecutionId);
        Assert.Equal(201, (int)retried.StatusCode);
        var retryResult = await retried.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", retryResult.GetProperty("result").GetString());
        Assert.Equal(failedExecutionId, retryResult.GetProperty("retryOfExecutionId").GetGuid());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/firmware");
        genealogyResponse.EnsureSuccessStatusCode();
        var executions = (await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("executions").EnumerateArray().ToArray();
        Assert.Equal(2, executions.Length);
        Assert.Contains(executions, item => item.GetProperty("result").GetString() == "Failed");
        Assert.Contains(executions, item =>
            item.GetProperty("result").GetString() == "Succeeded"
            && item.GetProperty("retryOfExecutionId").GetGuid() == failedExecutionId);
    }

    [SqlServerFact]
    public async Task EvidenceMismatchPreservesActualAlgorithmAndDeviceDiagnostic()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var failed = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-EVIDENCE-0901",
            checksumAlgorithm: "MD5",
            diagnosticCode: "DEVICE_VERIFY_17",
            diagnosticMessage: "Device reported a post-write verification failure.");

        Assert.Equal(201, (int)failed.StatusCode);
        var result = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", result.GetProperty("result").GetString());
        Assert.Equal("SHA-256", result.GetProperty("requiredChecksumAlgorithm").GetString());
        Assert.Equal("MD5", result.GetProperty("actualChecksumAlgorithm").GetString());
        Assert.Equal("DEVICE_VERIFY_17", result.GetProperty("reportedDiagnosticCode").GetString());
        Assert.Equal(
            "Device reported a post-write verification failure.",
            result.GetProperty("reportedDiagnosticMessage").GetString());
        Assert.Equal("FIRMWARE_EVIDENCE_MISMATCH", result.GetProperty("diagnosticCode").GetString());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/firmware");
        genealogyResponse.EnsureSuccessStatusCode();
        var execution = Assert.Single(
            (await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("executions").EnumerateArray());
        Assert.Equal("MD5", execution.GetProperty("actualChecksumAlgorithm").GetString());
        Assert.Equal("DEVICE_VERIFY_17", execution.GetProperty("reportedDiagnosticCode").GetString());
    }

    [SqlServerFact]
    public async Task OversizedDeviceDiagnosticReturnsStableValidationError()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var response = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-DIAGNOSTIC-LENGTH-0901",
            diagnosticCode: new string('D', 81),
            diagnosticMessage: "Device diagnostic");

        Assert.Equal(422, (int)response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FIRMWARE_EXECUTION_INVALID", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("correlationId").GetString()));
    }

    [SqlServerFact]
    public async Task RequiredFirmwareAtLaterOperationDoesNotBlockCurrentOperationCompletion()
    {
        var setup = await CreateSetupAsync(
            await server.CreateDatabaseAsync(),
            includeLaterFirmwareRequirement: true);
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var response = await ExecuteAsync(station, setup.FinishedSerial, "FW-MULTI-OP-0901");

        Assert.Equal(201, (int)response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("operationCompleted").GetBoolean());
        Assert.Equal("FUNCTION_TEST", result.GetProperty("nextOperationCode").GetString());
    }

    [SqlServerFact]
    public async Task IdempotentReplayReturnsOriginalExecutionAndChangedPayloadIsRejected()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var first = await ExecuteAsync(station, setup.FinishedSerial, "FW-IDEMPOTENT-0901");
        Assert.Equal(201, (int)first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await ExecuteAsync(station, setup.FinishedSerial, "FW-IDEMPOTENT-0901");
        Assert.Equal(200, (int)replay.StatusCode);
        var replayResult = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstResult.GetProperty("executionId").GetGuid(), replayResult.GetProperty("executionId").GetGuid());
        Assert.True(replayResult.GetProperty("isReplay").GetBoolean());

        var conflict = await ExecuteAsync(
            station,
            setup.FinishedSerial,
            "FW-IDEMPOTENT-0901",
            toolVersion: "5.2.1");
        Assert.Equal(409, (int)conflict.StatusCode);
        Assert.Equal(
            "FIRMWARE_IDEMPOTENCY_CONFLICT",
            (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task PausedOrderAndWrongOperationRejectExecutionWithoutFirmwareFacts()
    {
        var paused = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using (var context = CreateContext(paused.ConnectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [mes].[ProductionOrders]
                SET [Status] = N'Paused'
                WHERE [Id] = {paused.OrderId}
                """);
        }
        await using (var factory = CreateFactory(paused.ConnectionString))
        using (var station = factory.CreateClient())
        {
            await LoginAsync(station, "operator.09", OperatorPassword);
            var response = await ExecuteAsync(station, paused.FinishedSerial, "FW-PAUSED-0901");
            Assert.Equal(409, (int)response.StatusCode);
            Assert.Equal(
                "FIRMWARE_ORDER_STATUS_BLOCKED",
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var wrongOperation = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using (var context = CreateContext(wrongOperation.ConnectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [mes].[ProductIdentities]
                SET [NextOperationCode] = N'FUNCTION_TEST'
                WHERE [SerialNumber] = {wrongOperation.FinishedSerial}
                """);
        }
        await using (var factory = CreateFactory(wrongOperation.ConnectionString))
        using (var station = factory.CreateClient())
        {
            await LoginAsync(station, "operator.09", OperatorPassword);
            var response = await ExecuteAsync(station, wrongOperation.FinishedSerial, "FW-WRONG-OP-0901");
            Assert.Equal(409, (int)response.StatusCode);
            Assert.Equal(
                "FIRMWARE_OPERATION_MISMATCH",
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    [SqlServerFact]
    public async Task LegacySnapshotWithoutValidationEvidenceStopsWithoutInventingDefaults()
    {
        var setup = await CreateSetupAsync(
            await server.CreateDatabaseAsync(),
            omitFirmwareEvidence: true);
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var response = await ExecuteAsync(station, setup.FinishedSerial, "FW-LEGACY-0901");
        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal(
            "FIRMWARE_SNAPSHOT_REQUIREMENT_INVALID",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task QualityCanReadGenealogyButCannotExecuteFirmware()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var quality = factory.CreateClient();
        await LoginAsync(quality, "quality.09", QualityPassword);

        (await quality.GetAsync($"/api/genealogy/products/{setup.FinishedSerial}/firmware"))
            .EnsureSuccessStatusCode();
        var denied = await ExecuteAsync(quality, setup.FinishedSerial, "FW-QUALITY-0901");
        Assert.Equal(403, (int)denied.StatusCode);
        var denial = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("StationExecute", denial.GetProperty("capability").GetString());
        Assert.Contains("权限", denial.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(denial.GetProperty("correlationId").GetString()));
    }

    [SqlServerFact]
    public async Task ConcurrentSuccessfulExecutionsCreateOneSuccessFact()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var firstStation = factory.CreateClient();
        using var secondStation = factory.CreateClient();
        await LoginAsync(firstStation, "operator.09", OperatorPassword);
        await LoginAsync(secondStation, "operator.09", OperatorPassword);

        var responses = await Task.WhenAll(
            ExecuteAsync(firstStation, setup.FinishedSerial, "FW-CONCURRENT-A-0901"),
            ExecuteAsync(secondStation, setup.FinishedSerial, "FW-CONCURRENT-B-0901"));
        Assert.Single(responses, response => (int)response.StatusCode == 201);
        Assert.Single(responses, response => (int)response.StatusCode == 409);

        await using var context = CreateContext(setup.ConnectionString);
        Assert.Equal(
            1,
            await context.FirmwareConfigurationExecutions.CountAsync(item =>
                item.Result == FirmwareExecutionResult.Succeeded));
    }

    [SqlServerFact]
    public async Task DatabaseFailureRollsBackExecutionEventsAuditAndRouteAdvance()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using (var context = CreateContext(setup.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [audit].[TR_Ticket09_ForceAuditFailure]
                ON [audit].[BusinessAuditRecords]
                AFTER INSERT
                AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [Action] = 'FIRMWARE_CONFIGURATION_EXECUTE')
                        THROW 51909, 'Ticket 09 forced rollback.', 1;
                END;
                """);
        }
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.09", OperatorPassword);

        var response = await ExecuteAsync(station, setup.FinishedSerial, "FW-ROLLBACK-0901");
        Assert.Equal(500, (int)response.StatusCode);
        await using var verification = CreateContext(setup.ConnectionString);
        Assert.Empty(await verification.FirmwareConfigurationExecutions.ToArrayAsync());
        Assert.False(await verification.ManufacturingEvents.AnyAsync(item =>
            item.EventType == "FIRMWARE_CONFIGURATION_EXECUTED"
            || item.EventType == "FIRMWARE_CONFIGURATION_COMPLETED"));
        Assert.Equal(
            "FW_CONFIG",
            await verification.ProductIdentities
                .Where(item => item.SerialNumber == setup.FinishedSerial)
                .Select(item => item.NextOperationCode)
                .SingleAsync());
    }

    private static Task<HttpResponseMessage> ExecuteAsync(
        HttpClient station,
        string finishedSerial,
        string idempotencyKey,
        string checksum = "A1B2C3D4",
        string toolVersion = "5.2.0",
        Guid? retryOfExecutionId = null,
        string checksumAlgorithm = "SHA-256",
        string? diagnosticCode = null,
        string? diagnosticMessage = null) => station.PostAsJsonAsync(
        $"/api/execution/firmware/{finishedSerial}/executions",
        new
        {
            sourceSystem = "FW-STATION",
            idempotencyKey,
            requirementCode = "ROUTER-OS",
            actualVersion = "R1.4.7",
            configurationPackage = "CFG-FACTORY-A-3",
            checksumAlgorithm,
            checksumValue = checksum,
            toolId = "FLASHER-01",
            toolVersion,
            result = "Succeeded",
            diagnosticCode,
            diagnosticMessage,
            retryOfExecutionId,
            startedAtUtc = new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero),
            endedAtUtc = new DateTimeOffset(2026, 7, 30, 3, 2, 0, TimeSpan.Zero),
            location = "LINE-01/FW-01",
        });

    private static async Task<TestSetup> CreateSetupAsync(
        string connectionString,
        bool omitFirmwareEvidence = false,
        bool includeLaterFirmwareRequirement = false)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var account = Account("operator.09", "Ticket 09 operator", OperatorPassword, BusinessRole.Operator);
        var quality = Account("quality.09", "Ticket 09 quality engineer", QualityPassword, BusinessRole.QualityEngineer);
        var finished = new Material
        {
            Id = Guid.NewGuid(),
            Code = "ROUTER-FG-01",
            Name = "Industrial router",
            BaseUnit = "EA",
            TraceabilityMode = TraceabilityMode.Serial,
            IsActive = true,
        };
        context.AddRange(account, quality, finished);
        await context.SaveChangesAsync();

        var primaryFirmwareRequirement = new
        {
            code = "ROUTER-OS",
            version = "R1.4.7",
            required = true,
            evidenceReference = "Approved firmware baseline 09",
            operationCode = omitFirmwareEvidence ? null : "FW_CONFIG",
            configurationPackage = omitFirmwareEvidence ? null : "CFG-FACTORY-A-3",
            checksumAlgorithm = omitFirmwareEvidence ? null : "SHA-256",
            expectedChecksum = omitFirmwareEvidence ? null : "A1B2C3D4",
        };
        var firmwareRequirements = includeLaterFirmwareRequirement
            ?
            [
                primaryFirmwareRequirement,
                new
                {
                    code = "RADIO-MODULE",
                    version = "R2.0.0",
                    required = true,
                    evidenceReference = "Approved radio baseline 09",
                    operationCode = (string?)"FUNCTION_TEST",
                    configurationPackage = (string?)"CFG-RADIO-2",
                    checksumAlgorithm = (string?)"SHA-256",
                    expectedChecksum = (string?)"D4C3B2A1",
                },
            ]
            : new[] { primaryFirmwareRequirement };
        var definition = new
        {
            product = new
            {
                materialCode = finished.Code,
                materialName = finished.Name,
                sourceVersion = "PRODUCT-1.0",
                traceabilityMode = "Serial",
            },
            applicability = "Ticket 09 SQL Server integration test",
            bom = new { version = "BOM-1.0", components = Array.Empty<object>() },
            route = new
            {
                version = "ROUTE-1.0",
                operations = new[]
                {
                    new { sequence = 10, code = "START_WIP", name = "Start WIP" },
                    new { sequence = 20, code = "ASSEMBLY_BIND", name = "Assembly binding" },
                    new { sequence = 30, code = "FW_CONFIG", name = "Firmware configuration" },
                    new { sequence = 40, code = "FUNCTION_TEST", name = "Function test" },
                },
            },
            traceabilityPolicy = new { version = "TRACE-1.0", finishedProductMode = "Serial" },
            identityPolicy = new
            {
                version = "IDENTITY-1.0",
                finishedSerialSource = "ERP",
                requiredIdentifiers = new[] { "SerialNumber" },
                allocationTiming = "BeforeStartWip",
            },
            firmwareRequirements,
            testSpecifications = Array.Empty<object>(),
            completionGate = new { version = "GATE-1.0", requirements = new[] { "FIRMWARE_COMPLETE" } },
        };
        var definitionJson = JsonSerializer.Serialize(definition, WebJson);
        var template = new ProductExecutionTemplateVersion
        {
            Id = Guid.NewGuid(),
            MaterialId = finished.Id,
            Version = "FIRMWARE-1.0",
            Applicability = "Ticket 09 SQL Server integration test",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('9', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = account.Id,
        };
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "PO-FIRMWARE-0901",
            MaterialId = finished.Id,
            PlannedQuantity = 1,
            StartedQuantity = 1,
            Status = ProductionOrderStatus.InProduction,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = "PO-FIRMWARE-0901",
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
            CreatedByUserId = account.Id,
        };
        var identity = new ProductIdentity
        {
            Id = Guid.NewGuid(),
            MaterialId = finished.Id,
            SerialNumber = "ROUTER-SN-0901",
            SerialSourceType = IdentitySourceType.Erp,
            SerialSourceSystem = "ERP-U8",
            SerialSourceReference = "ERP-SN-0901",
            Status = ProductIdentityStatus.Bound,
            ProductionOrderId = order.Id,
            ExecutionSnapshotId = snapshot.Id,
            NextOperationCode = "FW_CONFIG",
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            BoundAtUtc = DateTimeOffset.UtcNow,
            StartSourceSystem = "MES-STATION",
            StartIdempotencyKey = "START-WIP-0901",
            StartCommandHash = new string('a', 64),
            AllocationSourceSystem = "ERP-U8",
            AllocationIdempotencyKey = "ALLOCATE-0901",
            AllocationCommandHash = new string('b', 64),
        };
        context.AddRange(template, order, snapshot, identity);
        await context.SaveChangesAsync();
        return new TestSetup(connectionString, order.Id, identity.SerialNumber);
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
            "IntegrationOnlySigningKey_09_AtLeast32Characters");
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

    private sealed record TestSetup(string ConnectionString, Guid OrderId, string FinishedSerial);
}
