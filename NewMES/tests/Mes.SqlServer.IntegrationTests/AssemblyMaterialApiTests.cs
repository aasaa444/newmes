using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class AssemblyMaterialApiTests(SqlServerFixture server)
{
    private const string HandlerPassword = "IntegrationOnly-Handler-08!";
    private const string OperatorPassword = "IntegrationOnly-Operator-08!";
    private const string QualityPassword = "IntegrationOnly-Quality-08!";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task SerialComponentBindingConsumesIssuedMaterialAndCompletesAssemblyRequirement()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-01",
            1m);

        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var response = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "ASSEMBLY-SERIAL-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = "ROUTER-PCBA-01",
                componentSerialNumber = "PCBA-SN-0001",
                lotNumber = "PCBA-LOT-01",
                quantity = 1m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero),
            });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Serial", result.GetProperty("traceabilityMode").GetString());
        Assert.NotEqual(Guid.Empty, result.GetProperty("bindingId").GetGuid());
        Assert.NotEqual(Guid.Empty, result.GetProperty("materialTransactionId").GetGuid());
        Assert.Equal(0m, result.GetProperty("remainingQuantity").GetDecimal());
        Assert.True(result.GetProperty("operationCompleted").GetBoolean());
        Assert.Equal("FW_CONFIG", result.GetProperty("nextOperationCode").GetString());

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirements = await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requirement = Assert.Single(requirements.GetProperty("requirements").EnumerateArray());
        Assert.Equal("ASSEMBLY_BIND", requirement.GetProperty("operationCode").GetString());
        Assert.Equal("Serial", requirement.GetProperty("traceabilityMode").GetString());
        Assert.Equal(1m, requirement.GetProperty("requiredQuantity").GetDecimal());
        Assert.Equal(1m, requirement.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(0m, requirement.GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(
            "PCBA-SN-0001",
            Assert.Single(requirement.GetProperty("activeBindings").EnumerateArray())
                .GetProperty("componentSerialNumber").GetString());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/materials");
        genealogyResponse.EnsureSuccessStatusCode();
        var genealogy = await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var consumption = Assert.Single(genealogy.GetProperty("consumptions").EnumerateArray());
        Assert.Equal("ROUTER-PCBA-01", consumption.GetProperty("materialCode").GetString());
        Assert.Equal("PCBA-SN-0001", consumption.GetProperty("componentSerialNumber").GetString());
        Assert.Equal("PCBA-LOT-01", consumption.GetProperty("lotNumber").GetString());
        Assert.Equal("Active", consumption.GetProperty("relationStatus").GetString());
    }

    [SqlServerFact]
    public async Task LotConsumptionRecordsActualQuantityAndSupportsLotImpactLookup()
    {
        var component = new TestComponent(
            "ROUTER-ENCLOSURE-01",
            "Router enclosure",
            TraceabilityMode.Lot,
            2m,
            "PerProductActual");
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync(), component);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            component.MaterialCode,
            "ENC-LOT-01",
            component.QuantityPer);

        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var response = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "ASSEMBLY-LOT-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = component.MaterialCode,
                lotNumber = "ENC-LOT-01",
                quantity = 2m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 5, 0, TimeSpan.Zero),
            });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Lot", result.GetProperty("traceabilityMode").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("bindingId").ValueKind);
        Assert.Equal(2m, result.GetProperty("consumedQuantity").GetDecimal());

        var impactResponse = await station.GetAsync(
            $"/api/genealogy/lots/ENC-LOT-01/products?materialCode={component.MaterialCode}");
        impactResponse.EnsureSuccessStatusCode();
        var impact = await impactResponse.Content.ReadFromJsonAsync<JsonElement>();
        var affected = Assert.Single(impact.GetProperty("affectedProducts").EnumerateArray());
        Assert.Equal(setup.FinishedSerial, affected.GetProperty("finishedSerialNumber").GetString());
        Assert.Equal(2m, affected.GetProperty("netConsumedQuantity").GetDecimal());
    }

    [SqlServerFact]
    public async Task QuantityBackflushUsesFrozenQuantityInsteadOfOperatorEnteredQuantity()
    {
        var component = new TestComponent(
            "ROUTER-SCREW-01",
            "Assembly screw",
            TraceabilityMode.None,
            4m,
            "OrderBackflush");
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync(), component);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            component.MaterialCode,
            "SCREW-LOT-01",
            component.QuantityPer);

        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var response = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "ASSEMBLY-BACKFLUSH-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = component.MaterialCode,
                lotNumber = "SCREW-LOT-01",
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 10, 0, TimeSpan.Zero),
            });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Quantity", result.GetProperty("traceabilityMode").GetString());
        Assert.Equal(4m, result.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(0m, result.GetProperty("remainingQuantity").GetDecimal());

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirements = await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requirement = Assert.Single(requirements.GetProperty("requirements").EnumerateArray());
        Assert.Equal("Quantity", requirement.GetProperty("traceabilityMode").GetString());
        Assert.Equal("OrderBackflush", requirement.GetProperty("consumptionRule").GetString());
        Assert.Empty(requirement.GetProperty("activeBindings").EnumerateArray());
    }

    [SqlServerFact]
    public async Task ConcurrentSerialBindingAllowsOnlyOneActiveProductRelation()
    {
        var setup = await CreateSetupAsync(
            await server.CreateDatabaseAsync(),
            productCount: 2);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-CONCURRENT",
            2m);
        using var firstStation = factory.CreateClient();
        using var secondStation = factory.CreateClient();
        await LoginAsync(firstStation, "operator.08", OperatorPassword);
        await LoginAsync(secondStation, "operator.08", OperatorPassword);

        var responses = await Task.WhenAll(
            ConsumeSerialAsync(
                firstStation,
                setup.FinishedSerials[0],
                "CONCURRENT-0801-A",
                "PCBA-SN-SHARED",
                "PCBA-LOT-CONCURRENT"),
            ConsumeSerialAsync(
                secondStation,
                setup.FinishedSerials[1],
                "CONCURRENT-0801-B",
                "PCBA-SN-SHARED",
                "PCBA-LOT-CONCURRENT"));

        Assert.Equal(
            [201, 409],
            responses.Select(item => (int)item.StatusCode).Order().ToArray());
        var rejected = responses.Single(item => (int)item.StatusCode == 409);
        Assert.Equal(
            "COMPONENT_SERIAL_ALREADY_BOUND",
            (await rejected.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());

        var impactResponse = await firstStation.GetAsync(
            "/api/genealogy/components/PCBA-SN-SHARED/products");
        impactResponse.EnsureSuccessStatusCode();
        var impact = await impactResponse.Content.ReadFromJsonAsync<JsonElement>();
        var relation = Assert.Single(impact.GetProperty("relationships").EnumerateArray());
        Assert.Equal("Active", relation.GetProperty("status").GetString());
    }

    [SqlServerFact]
    public async Task MissingTransferIssueOrBalanceLeavesNoPartialAssemblyFacts()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        using var station = factory.CreateClient();
        using var handler = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        await LoginAsync(handler, "handler.08", HandlerPassword);

        await AssertRejectedAsync(
            await ConsumeSerialAsync(
                station,
                setup.FinishedSerial,
                "NO-TRANSFER-0801",
                "PCBA-SN-NO-STOCK",
                "PCBA-LOT-ATOMIC"),
            "ASSEMBLY_MATERIAL_NOT_TRANSFERRED");
        await TransferAsync(handler, "ROUTER-PCBA-01", "PCBA-LOT-ATOMIC", 1m);
        await AssertRejectedAsync(
            await ConsumeSerialAsync(
                station,
                setup.FinishedSerial,
                "NO-ISSUE-0801",
                "PCBA-SN-NO-STOCK",
                "PCBA-LOT-ATOMIC"),
            "ASSEMBLY_MATERIAL_NOT_ISSUED");
        await IssueAsync(
            handler,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-ATOMIC",
            0.5m,
            "PARTIAL-ISSUE-0801");
        await AssertRejectedAsync(
            await ConsumeSerialAsync(
                station,
                setup.FinishedSerial,
                "INSUFFICIENT-0801",
                "PCBA-SN-NO-STOCK",
                "PCBA-LOT-ATOMIC"),
            "ASSEMBLY_MATERIAL_BALANCE_INSUFFICIENT");

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirements = await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requirement = Assert.Single(requirements.GetProperty("requirements").EnumerateArray());
        Assert.Equal(0m, requirement.GetProperty("consumedQuantity").GetDecimal());
        Assert.Empty(requirement.GetProperty("activeBindings").EnumerateArray());
    }

    [SqlServerFact]
    public async Task UnbindAndReplaceKeepOriginalRelationshipsAndCompensateConsumption()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-CORRECTION",
            1m);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var firstBinding = await ConsumeSerialAsync(
            station,
            setup.FinishedSerial,
            "BIND-CORRECTION-0801",
            "PCBA-SN-WRONG",
            "PCBA-LOT-CORRECTION");
        firstBinding.EnsureSuccessStatusCode();
        var firstResult = await firstBinding.Content.ReadFromJsonAsync<JsonElement>();
        var originalBindingId = firstResult.GetProperty("bindingId").GetGuid();

        var replacement = await station.PostAsJsonAsync(
            $"/api/execution/assembly/bindings/{originalBindingId}/replace",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "REPLACE-CORRECTION-0801",
                newComponentSerialNumber = "PCBA-SN-CORRECT",
                lotNumber = "PCBA-LOT-CORRECTION",
                reason = "Operator scanned the wrong component before downstream work",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 30, 0, TimeSpan.Zero),
            });

        replacement.EnsureSuccessStatusCode();
        var replacementResult = await replacement.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(originalBindingId, replacementResult.GetProperty("originalBindingId").GetGuid());
        Assert.NotEqual(Guid.Empty, replacementResult.GetProperty("replacementBindingId").GetGuid());
        Assert.NotEqual(Guid.Empty, replacementResult.GetProperty("reversalTransactionId").GetGuid());
        Assert.NotEqual(Guid.Empty, replacementResult.GetProperty("replacementTransactionId").GetGuid());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/materials");
        genealogyResponse.EnsureSuccessStatusCode();
        var genealogy = await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var consumptions = genealogy.GetProperty("consumptions").EnumerateArray().ToArray();
        Assert.Equal(3, consumptions.Length);
        Assert.Contains(consumptions, item =>
            item.GetProperty("componentSerialNumber").GetString() == "PCBA-SN-WRONG"
            && item.GetProperty("relationStatus").GetString() == "Unbound");
        Assert.Contains(consumptions, item =>
            item.GetProperty("componentSerialNumber").GetString() == "PCBA-SN-CORRECT"
            && item.GetProperty("relationStatus").GetString() == "Active");
        Assert.Contains(consumptions, item =>
            item.GetProperty("transactionType").GetString() == "Reversal"
            && item.GetProperty("reversesTransactionId").GetGuid()
                == replacementResult.GetProperty("originalTransactionId").GetGuid()
            && item.GetProperty("netConsumedQuantity").GetDecimal() == -1m);

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirements = await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requirement = Assert.Single(requirements.GetProperty("requirements").EnumerateArray());
        Assert.Equal(1m, requirement.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(
            "PCBA-SN-CORRECT",
            Assert.Single(requirement.GetProperty("activeBindings").EnumerateArray())
                .GetProperty("componentSerialNumber").GetString());
    }

    [SqlServerFact]
    public async Task UnbindRestoresRequirementAndIdempotentReplayDoesNotReverseTwice()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-UNBIND",
            1m);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var bound = await ConsumeSerialAsync(
            station,
            setup.FinishedSerial,
            "BIND-UNBIND-0801",
            "PCBA-SN-UNBIND",
            "PCBA-LOT-UNBIND");
        bound.EnsureSuccessStatusCode();
        var bindingId = (await bound.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("bindingId").GetGuid();
        var request = new
        {
            sourceSystem = "MES-STATION",
            idempotencyKey = "UNBIND-0801",
            reason = "Incorrect scan confirmed before downstream work",
            location = "LINE-01/ASSEMBLY-01",
            occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 40, 0, TimeSpan.Zero),
        };

        var unbound = await station.PostAsJsonAsync(
            $"/api/execution/assembly/bindings/{bindingId}/unbind",
            request);
        unbound.EnsureSuccessStatusCode();
        var firstResult = await unbound.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unbound", firstResult.GetProperty("status").GetString());
        Assert.Equal("ASSEMBLY_BIND", firstResult.GetProperty("nextOperationCode").GetString());
        var replay = await station.PostAsJsonAsync(
            $"/api/execution/assembly/bindings/{bindingId}/unbind",
            request);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isReplay").GetBoolean());

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirement = Assert.Single(
            (await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("requirements").EnumerateArray());
        Assert.Equal(0m, requirement.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(1m, requirement.GetProperty("remainingQuantity").GetDecimal());
        Assert.Empty(requirement.GetProperty("activeBindings").EnumerateArray());
    }

    [SqlServerFact]
    public async Task MaterialAndOperationMismatchReturnStableCodesWithoutPartialFacts()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-MISMATCH",
            1m);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);

        var wrongOperation = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "WRONG-OPERATION-0801",
                operationCode = "FW_CONFIG",
                materialCode = "ROUTER-PCBA-01",
                componentSerialNumber = "PCBA-SN-MISMATCH",
                lotNumber = "PCBA-LOT-MISMATCH",
                quantity = 1m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });
        await AssertRejectedAsync(wrongOperation, "ASSEMBLY_OPERATION_MISMATCH");
        var wrongMaterial = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "WRONG-MATERIAL-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = "ROUTER-NOT-IN-BOM",
                componentSerialNumber = "PCBA-SN-MISMATCH",
                lotNumber = "PCBA-LOT-MISMATCH",
                quantity = 1m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });
        Assert.Equal(422, (int)wrongMaterial.StatusCode);
        Assert.Equal(
            "ASSEMBLY_MATERIAL_NOT_REQUIRED",
            (await wrongMaterial.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());

        await using var verification = CreateContext(setup.ConnectionString);
        var deniedReasons = await verification.BusinessAuditRecords
            .Where(item => item.Action == "ASSEMBLY_MATERIAL_CONSUME"
                && item.Result == BusinessAuditResult.Denied)
            .Select(item => item.ReasonCode)
            .ToArrayAsync();
        Assert.Contains("ASSEMBLY_OPERATION_MISMATCH", deniedReasons);
        Assert.Contains("ASSEMBLY_MATERIAL_NOT_REQUIRED", deniedReasons);
    }

    [SqlServerFact]
    public async Task ReleasedSnapshotRemainsAuthoritativeWhenMaterialMasterChanges()
    {
        var component = new TestComponent(
            "ROUTER-ENCLOSURE-01",
            "Router enclosure",
            TraceabilityMode.Lot,
            1m,
            "PerProductActual");
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync(), component);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            component.MaterialCode,
            "ENC-LOT-FROZEN",
            1m);
        await using (var context = CreateContext(setup.ConnectionString))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [mes].[Materials]
                SET [IsActive] = 0,
                    [BaseUnit] = N'KG',
                    [TraceabilityMode] = N'Serial'
                WHERE [Code] = {component.MaterialCode}
                """);
        }

        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var response = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "FROZEN-SNAPSHOT-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = component.MaterialCode,
                lotNumber = "ENC-LOT-FROZEN",
                quantity = 1m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = DateTimeOffset.UtcNow,
            });

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "Lot",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("traceabilityMode").GetString());
    }

    [SqlServerFact]
    public async Task LegacySnapshotWithoutAssemblyRuleIsRejectedWithoutInventingDefaults()
    {
        var setup = await CreateSetupAsync(
            await server.CreateDatabaseAsync(),
            omitAssemblyRules: true);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-LEGACY",
            1m);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);

        var response = await ConsumeSerialAsync(
            station,
            setup.FinishedSerial,
            "LEGACY-SNAPSHOT-0801",
            "PCBA-SN-LEGACY",
            "PCBA-LOT-LEGACY");
        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal(
            "ASSEMBLY_SNAPSHOT_RULE_MISSING",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task QualityCanReadComponentImpactButCannotExecuteAssemblyCommands()
    {
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync());
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            "ROUTER-PCBA-01",
            "PCBA-LOT-RBAC",
            1m);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        (await ConsumeSerialAsync(
            station,
            setup.FinishedSerial,
            "BIND-RBAC-0801",
            "PCBA-SN-RBAC",
            "PCBA-LOT-RBAC")).EnsureSuccessStatusCode();

        using var quality = factory.CreateClient();
        await LoginAsync(quality, "quality.08", QualityPassword);
        var impact = await quality.GetAsync(
            "/api/genealogy/components/PCBA-SN-RBAC/products");
        impact.EnsureSuccessStatusCode();
        Assert.Single((await impact.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("relationships").EnumerateArray());
        var denied = await ConsumeSerialAsync(
            quality,
            setup.FinishedSerial,
            "QUALITY-WRITE-DENIED-0801",
            "PCBA-SN-RBAC-OTHER",
            "PCBA-LOT-RBAC");
        Assert.Equal(403, (int)denied.StatusCode);
        Assert.Equal(
            "StationExecute",
            (await denied.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("capability").GetString());
    }

    [SqlServerFact]
    public async Task LotConsumptionReversalRestoresRequirementAndRemainsVisibleInGenealogy()
    {
        var component = new TestComponent(
            "ROUTER-ENCLOSURE-01",
            "Router enclosure",
            TraceabilityMode.Lot,
            2m,
            "PerProductActual");
        var setup = await CreateSetupAsync(await server.CreateDatabaseAsync(), component);
        await using var factory = CreateFactory(setup.ConnectionString);
        await TransferAndIssueAsync(
            factory,
            setup.OrderId,
            component.MaterialCode,
            "ENC-LOT-REVERSE",
            component.QuantityPer);
        using var station = factory.CreateClient();
        await LoginAsync(station, "operator.08", OperatorPassword);
        var consumed = await station.PostAsJsonAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials/consume",
            new
            {
                sourceSystem = "MES-STATION",
                idempotencyKey = "LOT-CONSUME-REVERSE-0801",
                operationCode = "ASSEMBLY_BIND",
                materialCode = component.MaterialCode,
                lotNumber = "ENC-LOT-REVERSE",
                quantity = 2m,
                unit = "EA",
                location = "LINE-01/ASSEMBLY-01",
                occurredAtUtc = new DateTimeOffset(2026, 7, 30, 2, 0, 0, TimeSpan.Zero),
            });
        consumed.EnsureSuccessStatusCode();
        var originalTransactionId = (await consumed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("materialTransactionId").GetGuid();
        var reverseRequest = new
        {
            sourceSystem = "MES-STATION",
            idempotencyKey = "LOT-REVERSE-0801",
            reason = "Incorrect supplier lot was scanned before downstream work",
            location = "LINE-01/ASSEMBLY-01",
            occurredAtUtc = new DateTimeOffset(2026, 7, 30, 2, 5, 0, TimeSpan.Zero),
        };

        var reversed = await station.PostAsJsonAsync(
            $"/api/execution/assembly/consumptions/{originalTransactionId}/reverse",
            reverseRequest);
        reversed.EnsureSuccessStatusCode();
        var reversedResult = await reversed.Content.ReadFromJsonAsync<JsonElement>();
        var reversalTransactionId = reversedResult.GetProperty("reversalTransactionId").GetGuid();
        Assert.Equal(originalTransactionId, reversedResult.GetProperty("originalTransactionId").GetGuid());
        Assert.Equal("ASSEMBLY_BIND", reversedResult.GetProperty("nextOperationCode").GetString());
        var replay = await station.PostAsJsonAsync(
            $"/api/execution/assembly/consumptions/{originalTransactionId}/reverse",
            reverseRequest);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isReplay").GetBoolean());

        var genealogyResponse = await station.GetAsync(
            $"/api/genealogy/products/{setup.FinishedSerial}/materials");
        genealogyResponse.EnsureSuccessStatusCode();
        var genealogy = await genealogyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var facts = genealogy.GetProperty("consumptions").EnumerateArray().ToArray();
        Assert.Equal(2, facts.Length);
        var reversal = Assert.Single(facts, item =>
            item.GetProperty("transactionType").GetString() == "Reversal");
        Assert.Equal(reversalTransactionId, reversal.GetProperty("materialTransactionId").GetGuid());
        Assert.Equal(originalTransactionId, reversal.GetProperty("reversesTransactionId").GetGuid());
        Assert.Equal(
            reverseRequest.reason,
            reversal.GetProperty("reason").GetString());
        Assert.Equal(-2m, reversal.GetProperty("netConsumedQuantity").GetDecimal());

        var requirementsResponse = await station.GetAsync(
            $"/api/execution/assembly/{setup.FinishedSerial}/materials");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirement = Assert.Single(
            (await requirementsResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("requirements").EnumerateArray());
        Assert.Equal(0m, requirement.GetProperty("consumedQuantity").GetDecimal());
        var lotImpactResponse = await station.GetAsync(
            $"/api/genealogy/lots/ENC-LOT-REVERSE/products?materialCode={component.MaterialCode}");
        lotImpactResponse.EnsureSuccessStatusCode();
        Assert.Empty((await lotImpactResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("affectedProducts").EnumerateArray());
    }

    private static async Task<TestSetup> CreateSetupAsync(
        string connectionString,
        TestComponent? component = null,
        int productCount = 1,
        bool omitAssemblyRules = false)
    {
        component ??= new TestComponent(
            "ROUTER-PCBA-01",
            "Tested PCBA",
            TraceabilityMode.Serial,
            1m,
            "PerProductActual");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var handler = Account(
            "handler.08",
            "Ticket 08 material handler",
            HandlerPassword,
            BusinessRole.MaterialHandler);
        var operatorAccount = Account(
            "operator.08",
            "Ticket 08 operator",
            OperatorPassword,
            BusinessRole.Operator);
        var qualityAccount = Account(
            "quality.08",
            "Ticket 08 quality engineer",
            QualityPassword,
            BusinessRole.QualityEngineer);
        var finished = Material("ROUTER-FG-01", "Industrial router", TraceabilityMode.Serial);
        var componentMaterial = Material(
            component.MaterialCode,
            component.MaterialName,
            component.TraceabilityMode);
        context.AddRange(handler, operatorAccount, qualityAccount, finished, componentMaterial);
        await context.SaveChangesAsync();

        var definition = new ExecutionTemplateDefinition(
            new ProductDefinition(finished.Code, finished.Name, "PRODUCT-1.0", "Serial"),
            "Ticket 08 SQL Server integration test",
            new BomDefinition(
                "BOM-1.0",
                [new BomComponentDefinition(
                    componentMaterial.Code,
                    component.QuantityPer,
                    "EA",
                    component.TraceabilityMode.ToString(),
                    omitAssemblyRules ? null : "ASSEMBLY_BIND",
                    omitAssemblyRules ? null : component.ConsumptionRule)]),
            new RouteDefinition("ROUTE-1.0", [
                new RouteOperationDefinition(10, "START_WIP", "Start WIP"),
                new RouteOperationDefinition(20, "ASSEMBLY_BIND", "Assembly binding"),
                new RouteOperationDefinition(30, "FW_CONFIG", "Firmware configuration")]),
            new TraceabilityPolicyDefinition("TRACE-1.0", "Serial"),
            new IdentityPolicyDefinition("IDENTITY-1.0", "ERP", ["SerialNumber"]),
            [],
            [],
            new CompletionGateDefinition("GATE-1.0", ["ROUTE_COMPLETE"]));
        var definitionJson = JsonSerializer.Serialize(definition, WebJson);
        var template = new ProductExecutionTemplateVersion
        {
            Id = Guid.NewGuid(),
            MaterialId = finished.Id,
            Version = "ASSEMBLY-1.0",
            Applicability = "Ticket 08 SQL Server integration test",
            DefinitionJson = definitionJson,
            DefinitionHash = new string('8', 64),
            DefinitionHashAlgorithm = "SHA-256-JSON-V1",
            IsApproved = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = operatorAccount.Id,
        };
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "PO-ASSEMBLY-0801",
            MaterialId = finished.Id,
            PlannedQuantity = productCount,
            StartedQuantity = productCount,
            Status = ProductionOrderStatus.InProduction,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            SourceSystem = "ERP-U8",
            SourceReference = "PO-ASSEMBLY-0801",
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
        var identities = Enumerable.Range(1, productCount).Select(index => new ProductIdentity
        {
            Id = Guid.NewGuid(),
            MaterialId = finished.Id,
            SerialNumber = $"ROUTER-SN-08{index:D2}",
            SerialSourceType = IdentitySourceType.Erp,
            SerialSourceSystem = "ERP-U8",
            SerialSourceReference = $"ERP-SN-08{index:D2}",
            Status = ProductIdentityStatus.Bound,
            ProductionOrderId = order.Id,
            ExecutionSnapshotId = snapshot.Id,
            NextOperationCode = "ASSEMBLY_BIND",
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            BoundAtUtc = DateTimeOffset.UtcNow,
            StartSourceSystem = "MES-STATION",
            StartIdempotencyKey = $"START-WIP-08{index:D2}",
            StartCommandHash = new string('a', 64),
            AllocationSourceSystem = "ERP-U8",
            AllocationIdempotencyKey = $"ALLOCATE-08{index:D2}",
            AllocationCommandHash = new string('b', 64),
        }).ToArray();
        context.AddRange(template, order, snapshot);
        context.AddRange(identities);
        context.ManufacturingEvents.AddRange(identities.Select(identity => new ManufacturingEvent
        {
            Id = Guid.NewGuid(),
            EventType = "IDENTITY_ALLOCATED",
            AggregateType = "ProductIdentity",
            AggregateId = identity.Id.ToString(),
            OccurredAtUtc = identity.AllocatedAtUtc,
            RecordedAtUtc = identity.AllocatedAtUtc,
            Actor = "erp.identity.adapter",
            PayloadJson = JsonSerializer.Serialize(new { identity.SerialNumber }, WebJson),
            ProductIdentityId = identity.Id,
            CorrelationId = $"allocation-{identity.Id}",
        }));
        await context.SaveChangesAsync();
        return new TestSetup(
            connectionString,
            order.Id,
            identities.Select(item => item.SerialNumber).ToArray());
    }

    private static async Task TransferAndIssueAsync(
        WebApplicationFactory<Program> factory,
        Guid orderId,
        string materialCode,
        string lotNumber,
        decimal quantity)
    {
        using var handler = factory.CreateClient();
        await LoginAsync(handler, "handler.08", HandlerPassword);
        await TransferAsync(handler, materialCode, lotNumber, quantity);
        await IssueAsync(
            handler,
            orderId,
            materialCode,
            lotNumber,
            quantity,
            $"ISSUE-{materialCode}-{lotNumber}");
    }

    private static Task<HttpResponseMessage> ConsumeSerialAsync(
        HttpClient station,
        string finishedSerial,
        string idempotencyKey,
        string componentSerialNumber,
        string lotNumber) => station.PostAsJsonAsync(
        $"/api/execution/assembly/{finishedSerial}/materials/consume",
        new
        {
            sourceSystem = "MES-STATION",
            idempotencyKey,
            operationCode = "ASSEMBLY_BIND",
            materialCode = "ROUTER-PCBA-01",
            componentSerialNumber,
            lotNumber,
            quantity = 1m,
            unit = "EA",
            location = "LINE-01/ASSEMBLY-01",
            occurredAtUtc = new DateTimeOffset(2026, 7, 30, 1, 20, 0, TimeSpan.Zero),
        });

    private static async Task TransferAsync(
        HttpClient handler,
        string materialCode,
        string lotNumber,
        decimal quantity)
    {
        var response = await handler.PostAsJsonAsync("/api/material/line-side-transfers", new
        {
            contractVersion = "1.0",
            sourceSystem = "WMS-DEMO",
            idempotencyKey = $"TRANSFER-{materialCode}-{lotNumber}",
            sourceDocumentType = "LINE_SIDE_TRANSFER",
            sourceDocumentNumber = $"TRANSFER-{lotNumber}",
            materialCode,
            lotNumber,
            quantity,
            unit = "EA",
            fromParty = "Raw material warehouse",
            toParty = "Line side",
            occurredAtUtc = new DateTimeOffset(2026, 7, 30, 0, 30, 0, TimeSpan.Zero),
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task IssueAsync(
        HttpClient handler,
        Guid orderId,
        string materialCode,
        string lotNumber,
        decimal quantity,
        string idempotencyKey)
    {
        var response = await handler.PostAsJsonAsync("/api/material/order-issues", new
        {
            contractVersion = "1.0",
            sourceSystem = "MES-MATERIAL-WORKBENCH",
            idempotencyKey,
            productionOrderId = orderId,
            materialCode,
            lotNumber,
            quantity,
            unit = "EA",
            sourceDocumentNumber = $"PICK-{lotNumber}",
            occurredAtUtc = new DateTimeOffset(2026, 7, 30, 0, 30, 0, TimeSpan.Zero),
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task AssertRejectedAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        Assert.Equal(409, (int)response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, payload.GetProperty("code").GetString());
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
        BaseUnit = "EA",
        TraceabilityMode = mode,
        IsActive = true,
    };

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__MesDatabase", connectionString);
        Environment.SetEnvironmentVariable(
            "Security__JwtSigningKey",
            "IntegrationOnlySigningKey_08_AtLeast32Characters");
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

    private sealed record TestSetup(
        string ConnectionString,
        Guid OrderId,
        IReadOnlyList<string> FinishedSerials)
    {
        public string FinishedSerial => FinishedSerials[0];
    }

    private sealed record TestComponent(
        string MaterialCode,
        string MaterialName,
        TraceabilityMode TraceabilityMode,
        decimal QuantityPer,
        string ConsumptionRule);
}
