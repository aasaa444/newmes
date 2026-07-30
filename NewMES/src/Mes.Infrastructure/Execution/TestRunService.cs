using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Quality;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

/// <summary>
/// 按订单快照中的已批准规范评估逐项测量并保存一次不可变测试运行。
/// 失败后的复测创建新运行并关联前次结果，不覆盖原失败证据。
/// </summary>
public sealed class TestRunService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string ExecuteAction = "TEST_RUN_EXECUTE";
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private const decimal MaximumStoredNumeric = 999999999999.999999m;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);
    private readonly QualityHoldRecorder qualityHoldRecorder = new(context, timeProvider);

    public async Task<TestRunView> ExecuteAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        TestRunRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var serialNumber = Normalize(finishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            ExecuteAction,
            "ProductIdentity",
            serialNumber,
            correlationId,
            cancellationToken);
        try
        {
            var command = Parse(request, serialNumber);
            // 测量、总体判定、复测关系、命令回执和审计作为一个事实包原子提交。
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var replay = await context.TestRuns
                    .AsNoTracking()
                    .Include(run => run.ProductIdentity)
                    .Include(run => run.Measurements)
                    .SingleOrDefaultAsync(
                        run => run.SourceSystem == command.SourceSystem
                            && run.IdempotencyKey == command.IdempotencyKey,
                        cancellationToken);
                if (replay is not null)
                {
                    if (!string.Equals(replay.CommandHash, command.CommandHash, StringComparison.Ordinal))
                    {
                        throw Rejected(
                            "TEST_RUN_IDEMPOTENCY_CONFLICT",
                            "相同来源系统和幂等键已用于不同的测试执行，请核对测试台原始命令。",
                            409);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return ToView(replay, replay.ProductIdentity!.SerialNumber, true);
                }

                var identity = await context.ProductIdentities
                    .Include(item => item.ProductionOrder)
                    .Include(item => item.ExecutionSnapshot)
                    .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken)
                    ?? throw Rejected("PRODUCT_IDENTITY_NOT_FOUND", "未找到成品 SN，请核对扫码内容和身份分配记录。", 404);
                EnsureExecutable(identity);
                if (await QualityHoldGuard.IsActiveAsync(context, identity.Id, cancellationToken))
                {
                    // 幂等回放在此门禁之前返回；任何新测试（包括未授权复测）都必须等待正式质量处置。
                    throw Rejected(
                        "QUALITY_HOLD_ACTIVE",
                        "该成品处于质量保留，必须完成授权处置后才能继续测试或复测。",
                        409);
                }

                var definition = DeserializeDefinition(identity.ExecutionSnapshot!.DefinitionJson);
                var specification = definition.TestSpecifications.SingleOrDefault(item =>
                    string.Equals(item.Code, command.SpecificationCode, StringComparison.Ordinal))
                    ?? throw Rejected(
                        "TEST_SPECIFICATION_NOT_IN_SNAPSHOT",
                        "订单冻结快照不包含该测试规范，请核对订单下达版本。",
                        422);
                EnsureFrozenSpecificationValid(specification, definition);
                if (!string.Equals(identity.NextOperationCode, specification.OperationCode, StringComparison.Ordinal))
                {
                    throw Rejected(
                        "TEST_OPERATION_MISMATCH",
                        $"成品当前工序为 {identity.NextOperationCode ?? "<无>"}，不能确认本次测试。",
                        409);
                }

                if (await context.TestRuns.AnyAsync(
                        run => run.ProductIdentityId == identity.Id
                            && run.SpecificationCode == specification.Code
                            && run.SpecificationVersion == specification.Version
                            && run.Result == TestRunResult.Succeeded,
                        cancellationToken))
                {
                    throw Rejected(
                        "TEST_SPECIFICATION_ALREADY_SUCCEEDED",
                        "该成品已通过此冻结规范，不能重复生成成功事实。",
                        409);
                }

                var latestFailure = await context.TestRuns
                    .AsNoTracking()
                    .Where(run => run.ProductIdentityId == identity.Id
                        && run.SpecificationCode == specification.Code
                        && run.SpecificationVersion == specification.Version
                        && run.Result == TestRunResult.Failed)
                    .OrderByDescending(run => run.RecordedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                var retryOf = await ResolveRetryAsync(
                    command.RetryOfTestRunId,
                    latestFailure,
                    identity.Id,
                    specification,
                    cancellationToken);

                var evaluated = EvaluateMeasurements(specification.Items!, command.Measurements);
                var result = evaluated.All(item => item.Result == TestMeasurementResult.Passed)
                    ? TestRunResult.Succeeded
                    : TestRunResult.Failed;
                var diagnosticCode = result == TestRunResult.Failed ? "TEST_MEASUREMENT_FAILED" : null;
                var diagnosticMessage = result == TestRunResult.Failed
                    ? "一项或多项测量未通过订单冻结规范。"
                    : null;
                var now = timeProvider.GetUtcNow();
                var runEvent = AppendEvent(
                    "TEST_RUN_RECORDED",
                    identity,
                    actor,
                    command.EndedAtUtc,
                    now,
                    command.Location,
                    correlationId,
                    new
                    {
                        specificationCode = specification.Code,
                        specificationVersion = specification.Version,
                        specificationDefinitionHash = specification.DefinitionHash,
                        operationCode = specification.OperationCode,
                        result = result.ToString(),
                        retryOfTestRunId = retryOf?.Id,
                        command.DeviceId,
                        command.DeviceVersion,
                        command.FixtureId,
                        command.FixtureVersion,
                        command.RawReportReference,
                    },
                    retryOf?.ManufacturingEventId);

                var operationCompleted = false;
                if (result == TestRunResult.Succeeded)
                {
                    var otherRequired = definition.TestSpecifications
                        .Where(item => item.Required
                            && string.Equals(item.OperationCode, specification.OperationCode, StringComparison.Ordinal)
                            && !string.Equals(item.Code, specification.Code, StringComparison.Ordinal))
                        .Select(item => new { item.Code, item.Version })
                        .ToArray();
                    var completed = await context.TestRuns
                        .Where(run => run.ProductIdentityId == identity.Id
                            && run.Result == TestRunResult.Succeeded)
                        .Select(run => new { run.SpecificationCode, run.SpecificationVersion })
                        .ToArrayAsync(cancellationToken);
                    operationCompleted = otherRequired.All(required => completed.Any(item =>
                        item.SpecificationCode == required.Code
                        && item.SpecificationVersion == required.Version));
                    if (operationCompleted)
                    {
                        identity.NextOperationCode = NextOperationCode(definition, specification.OperationCode!);
                        AppendEvent(
                            "TEST_OPERATION_COMPLETED",
                            identity,
                            actor,
                            command.EndedAtUtc,
                            now,
                            command.Location,
                            correlationId,
                            new
                            {
                                operationCode = specification.OperationCode,
                                nextOperationCode = identity.NextOperationCode,
                            },
                            runEvent.Id);
                    }
                }

                var run = new TestRun
                {
                    Id = Guid.NewGuid(),
                    ProductIdentityId = identity.Id,
                    ProductionOrderId = identity.ProductionOrderId!.Value,
                    ExecutionSnapshotId = identity.ExecutionSnapshotId!.Value,
                    SpecificationCode = specification.Code,
                    SpecificationVersion = specification.Version,
                    SpecificationDefinitionHash = specification.DefinitionHash!,
                    OperationCode = specification.OperationCode!,
                    DeviceId = command.DeviceId,
                    DeviceVersion = command.DeviceVersion,
                    FixtureId = command.FixtureId,
                    FixtureVersion = command.FixtureVersion,
                    RawReportReference = command.RawReportReference,
                    Result = result,
                    DiagnosticCode = diagnosticCode,
                    DiagnosticMessage = diagnosticMessage,
                    RetryOfTestRunId = retryOf?.Id,
                    ManufacturingEventId = runEvent.Id,
                    SourceSystem = command.SourceSystem,
                    IdempotencyKey = command.IdempotencyKey,
                    CommandHash = command.CommandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    ActorUserId = actor.UserId,
                    ActorUsername = actor.Username,
                    StartedAtUtc = command.StartedAtUtc,
                    EndedAtUtc = command.EndedAtUtc,
                    RecordedAtUtc = now,
                    Location = command.Location,
                    CorrelationId = correlationId,
                    OperationCompleted = operationCompleted,
                    NextOperationCodeAfter = identity.NextOperationCode,
                };
                foreach (var measurement in evaluated)
                {
                    run.Measurements.Add(new TestMeasurement
                    {
                        Id = Guid.NewGuid(),
                        TestRunId = run.Id,
                        ItemCode = measurement.Item.Code!,
                        ItemName = measurement.Item.Name!,
                        DataType = measurement.Item.DataType!,
                        Required = measurement.Item.Required,
                        RawValue = measurement.Request.RawValue!,
                        Unit = NullIfEmpty(measurement.Request.Unit),
                        DecimalPlaces = measurement.Item.DecimalPlaces,
                        LowerLimit = measurement.Item.LowerLimit,
                        UpperLimit = measurement.Item.UpperLimit,
                        ExpectedText = measurement.Item.ExpectedText,
                        ExpectedBoolean = measurement.Item.ExpectedBoolean,
                        NumericValue = measurement.NumericValue,
                        BooleanValue = measurement.BooleanValue,
                        Result = measurement.Result,
                        DiagnosticCode = measurement.DiagnosticCode,
                        DiagnosticMessage = measurement.DiagnosticMessage,
                        ReportedDiagnosticCode = NullIfEmpty(measurement.Request.DiagnosticCode),
                        ReportedDiagnosticMessage = NullIfEmpty(measurement.Request.DiagnosticMessage),
                    });
                }

                context.TestRuns.Add(run);
                if (run.Result == TestRunResult.Failed)
                {
                    // 测试失败、不合格、保留、订单计数和审计必须随同一次测试事务原子落库。
                    await qualityHoldRecorder.RecordTestFailureAsync(
                        identity,
                        run,
                        actor,
                        correlationId,
                        cancellationToken);
                }
                AppendAudit(actor, ExecuteAction, identity.Id.ToString(), BusinessAuditResult.Succeeded, null, correlationId);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToView(run, identity.SerialNumber, false);
            });
        }
        catch (TestRunRejectedException error)
        {
            context.ChangeTracker.Clear();
            AppendAudit(actor, ExecuteAction, serialNumber, BusinessAuditResult.Denied, error.Code, correlationId);
            await context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TestWorkstationView> ReadWorkstationAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadIdentityAsync(
            actor,
            BusinessCapability.StationExecute,
            "TEST_WORKSTATION_READ",
            finishedSerialNumber,
            correlationId,
            cancellationToken);
        var definition = DeserializeDefinition(identity.ExecutionSnapshot!.DefinitionJson);
        var runs = await ReadRunsAsync(identity.Id, cancellationToken);
        var specifications = definition.TestSpecifications.Select(specification =>
        {
            EnsureFrozenSpecificationValid(specification, definition);
            var history = runs.Where(run => run.SpecificationCode == specification.Code
                && run.SpecificationVersion == specification.Version).ToArray();
            return new TestSpecificationWorkstationView(
                specification.Code,
                specification.Version,
                specification.OperationCode!,
                specification.Required,
                specification.EvidenceReference,
                specification.DefinitionHash!,
                history.Any(run => run.Result == "Succeeded")
                    ? "Succeeded"
                    : history.Length > 0 ? "Failed" : "Pending",
                specification.Items!,
                history);
        }).ToArray();
        return new TestWorkstationView(
            identity.Id,
            identity.SerialNumber,
            identity.NextOperationCode,
            specifications);
    }

    public async Task<TestGenealogyView> ReadGenealogyAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadIdentityAsync(
            actor,
            BusinessCapability.GenealogyRead,
            "TEST_GENEALOGY_READ",
            finishedSerialNumber,
            correlationId,
            cancellationToken);
        return new TestGenealogyView(
            identity.Id,
            identity.SerialNumber,
            identity.ProductionOrderId!.Value,
            await ReadRunsAsync(identity.Id, cancellationToken));
    }

    private async Task<ProductIdentity> LoadIdentityAsync(
        EffectiveIdentity actor,
        BusinessCapability capability,
        string action,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var serialNumber = Normalize(finishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            capability,
            action,
            "ProductIdentity",
            serialNumber,
            correlationId,
            cancellationToken);
        return await context.ProductIdentities
            .AsNoTracking()
            .Include(item => item.ExecutionSnapshot)
            .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken) is
        { ProductionOrderId: not null, ExecutionSnapshot: not null } identity
            ? identity
            : throw Rejected("TEST_PRODUCT_NOT_IN_WIP", "未找到已投产且带执行快照的成品 SN。", 404);
    }

    private async Task<IReadOnlyList<TestRunView>> ReadRunsAsync(
        Guid productIdentityId,
        CancellationToken cancellationToken)
    {
        var runs = await context.TestRuns
            .AsNoTracking()
            .Include(run => run.ProductIdentity)
            .Include(run => run.Measurements)
            .Where(run => run.ProductIdentityId == productIdentityId)
            .OrderBy(run => run.RecordedAtUtc)
            .ToArrayAsync(cancellationToken);
        return runs.Select(run => ToView(run, run.ProductIdentity!.SerialNumber, false)).ToArray();
    }

    private async Task<TestRun?> ResolveRetryAsync(
        Guid? retryOfTestRunId,
        TestRun? latestFailure,
        Guid productIdentityId,
        TestSpecificationReferenceDefinition specification,
        CancellationToken cancellationToken)
    {
        if (latestFailure is not null && retryOfTestRunId is null)
        {
            throw Rejected(
                "TEST_RETRY_REFERENCE_REQUIRED",
                "该规范已有失败执行，复测必须引用最新失败记录。",
                409);
        }

        if (retryOfTestRunId is null)
        {
            return null;
        }

        var retryOf = await context.TestRuns.SingleOrDefaultAsync(
            run => run.Id == retryOfTestRunId
                && run.ProductIdentityId == productIdentityId
                && run.SpecificationCode == specification.Code
                && run.SpecificationVersion == specification.Version,
            cancellationToken) ?? throw Rejected(
                "TEST_RETRY_REFERENCE_INVALID",
                "复测引用不属于当前成品和冻结规范，请核对原失败记录。",
                422);
        if (retryOf.Result != TestRunResult.Failed)
        {
            throw Rejected("TEST_RETRY_REFERENCE_INVALID", "复测只能引用失败的测试执行。", 422);
        }

        if (latestFailure is not null && retryOf.Id != latestFailure.Id)
        {
            throw Rejected("TEST_RETRY_REFERENCE_STALE", "复测必须引用该规范最新的失败执行。", 409);
        }

        return retryOf;
    }

    private static EvaluatedMeasurement[] EvaluateMeasurements(
        IReadOnlyList<TestSpecificationItemDefinition> items,
        IReadOnlyList<TestMeasurementRequest> submitted)
    {
        var duplicate = submitted
            .GroupBy(item => Normalize(item.ItemCode), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw Rejected("TEST_MEASUREMENT_DUPLICATE", $"测试项目 {duplicate.Key} 被重复提交，请核对设备数据。", 422);
        }

        var requests = submitted.ToDictionary(item => Normalize(item.ItemCode), StringComparer.Ordinal);
        var unknown = requests.Keys.FirstOrDefault(code => !items.Any(item => item.Code == code));
        if (unknown is not null)
        {
            throw Rejected("TEST_MEASUREMENT_UNKNOWN", $"测试项目 {unknown} 不在订单冻结规范中。", 422);
        }

        return items
            .Where(item => item.Required || requests.ContainsKey(item.Code!))
            .Select(item => requests.TryGetValue(item.Code!, out var request)
                ? EvaluateMeasurement(item, request)
                : new EvaluatedMeasurement(
                    item,
                    new TestMeasurementRequest(item.Code, string.Empty, item.Unit),
                    null,
                    null,
                    TestMeasurementResult.Failed,
                    "TEST_MEASUREMENT_REQUIRED",
                    $"必测项目 {item.Code} 缺失，请检查测试台采集与映射配置。"))
            .ToArray();
    }

    private static EvaluatedMeasurement EvaluateMeasurement(
        TestSpecificationItemDefinition item,
        TestMeasurementRequest request)
    {
        var rawValue = request.RawValue;
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Length > 1000)
        {
            throw Rejected("TEST_MEASUREMENT_VALUE_INVALID", $"测试项目 {item.Code} 缺少有效原始值。", 422);
        }

        if (!string.Equals(NullIfEmpty(request.Unit), NullIfEmpty(item.Unit), StringComparison.Ordinal))
        {
            throw Rejected("TEST_MEASUREMENT_UNIT_MISMATCH", $"测试项目 {item.Code} 的单位与冻结规范不一致。", 422);
        }

        return item.DataType switch
        {
            "Numeric" => EvaluateNumeric(item, request, rawValue),
            "Text" => EvaluateText(item, request, rawValue),
            "Boolean" => EvaluateBoolean(item, request, rawValue),
            _ => throw Rejected(
                "TEST_SNAPSHOT_ITEM_INVALID",
                $"冻结测试项目 {item.Code} 使用了不支持的数据类型，请停止执行并核对规范。",
                500),
        };
    }

    private static EvaluatedMeasurement EvaluateNumeric(
        TestSpecificationItemDefinition item,
        TestMeasurementRequest request,
        string rawValue)
    {
        if (!decimal.TryParse(rawValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw Rejected("TEST_MEASUREMENT_TYPE_MISMATCH", $"测试项目 {item.Code} 必须提交十进制数值。", 422);
        }

        if (value is < -MaximumStoredNumeric or > MaximumStoredNumeric)
        {
            throw Rejected(
                "TEST_MEASUREMENT_VALUE_OUT_OF_STORAGE_RANGE",
                $"测试项目 {item.Code} 超出 SQL Server decimal(18,6) 可保存范围。",
                422);
        }

        var scale = rawValue.Contains('.', StringComparison.Ordinal)
            ? rawValue.Length - rawValue.IndexOf('.', StringComparison.Ordinal) - 1
            : 0;
        if (item.DecimalPlaces is not null && scale > item.DecimalPlaces.Value)
        {
            throw Rejected("TEST_MEASUREMENT_PRECISION_EXCEEDED", $"测试项目 {item.Code} 超出冻结规范允许的小数位数。", 422);
        }

        var passed = (item.LowerLimit is null || value >= item.LowerLimit)
            && (item.UpperLimit is null || value <= item.UpperLimit);
        return new EvaluatedMeasurement(
            item,
            request,
            value,
            null,
            passed ? TestMeasurementResult.Passed : TestMeasurementResult.Failed,
            passed ? null : "TEST_NUMERIC_OUT_OF_RANGE",
            passed ? null : "数值不在冻结规范的包含边界范围内。");
    }

    private static EvaluatedMeasurement EvaluateText(
        TestSpecificationItemDefinition item,
        TestMeasurementRequest request,
        string rawValue)
    {
        var passed = string.Equals(rawValue, item.ExpectedText, StringComparison.Ordinal);
        return new EvaluatedMeasurement(
            item,
            request,
            null,
            null,
            passed ? TestMeasurementResult.Passed : TestMeasurementResult.Failed,
            passed ? null : "TEST_TEXT_MISMATCH",
            passed ? null : "文本值与冻结规范的期望值不一致。");
    }

    private static EvaluatedMeasurement EvaluateBoolean(
        TestSpecificationItemDefinition item,
        TestMeasurementRequest request,
        string rawValue)
    {
        if (!bool.TryParse(rawValue, out var value))
        {
            throw Rejected("TEST_MEASUREMENT_TYPE_MISMATCH", $"测试项目 {item.Code} 必须提交 true 或 false。", 422);
        }

        var passed = value == item.ExpectedBoolean;
        return new EvaluatedMeasurement(
            item,
            request,
            null,
            value,
            passed ? TestMeasurementResult.Passed : TestMeasurementResult.Failed,
            passed ? null : "TEST_BOOLEAN_MISMATCH",
            passed ? null : "布尔值与冻结规范的期望值不一致。");
    }

    private static void EnsureExecutable(ProductIdentity identity)
    {
        if (identity.Status != ProductIdentityStatus.Bound
            || identity.ProductionOrder is null
            || identity.ExecutionSnapshot is null
            || identity.ProductionOrderId is null
            || identity.ExecutionSnapshotId is null)
        {
            throw Rejected("TEST_PRODUCT_NOT_IN_WIP", "成品尚未绑定可执行订单快照，不能确认测试。", 409);
        }

        if (identity.ProductionOrder.Status != ProductionOrderStatus.InProduction)
        {
            throw Rejected("TEST_ORDER_STATUS_BLOCKED", "生产订单当前不是执行中状态，不能确认测试。", 409);
        }
    }

    private static void EnsureFrozenSpecificationValid(
        TestSpecificationReferenceDefinition specification,
        ExecutionTemplateDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(specification.OperationCode)
            || string.IsNullOrWhiteSpace(specification.MaterialCode)
            || string.IsNullOrWhiteSpace(specification.DefinitionHash)
            || specification.Items is not { Count: > 0 }
            || !string.Equals(specification.MaterialCode, definition.Product.MaterialCode, StringComparison.Ordinal)
            || !definition.Route.Operations.Any(item => item.Code == specification.OperationCode)
            || specification.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.DataType)))
        {
            throw Rejected(
                "TEST_SNAPSHOT_SPECIFICATION_INVALID",
                "订单快照中的测试规范不完整或不适用，请停止执行并核对下达版本。",
                500);
        }
    }

    private ManufacturingEvent AppendEvent(
        string eventType,
        ProductIdentity identity,
        EffectiveIdentity actor,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string location,
        string correlationId,
        object payload,
        Guid? causationEventId = null)
    {
        var manufacturingEvent = new ManufacturingEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            AggregateType = "ProductIdentity",
            AggregateId = identity.Id.ToString(),
            OccurredAtUtc = occurredAtUtc,
            RecordedAtUtc = recordedAtUtc,
            Actor = actor.Username,
            PayloadJson = JsonSerializer.Serialize(payload, WebJson),
            ProductIdentityId = identity.Id,
            ProductionOrderId = identity.ProductionOrderId,
            ExecutionSnapshotId = identity.ExecutionSnapshotId,
            Location = location,
            CorrelationId = correlationId,
            CausationEventId = causationEventId,
        };
        context.ManufacturingEvents.Add(manufacturingEvent);
        return manufacturingEvent;
    }

    private void AppendAudit(
        EffectiveIdentity actor,
        string action,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.Operator,
            BusinessCapability.StationExecute,
            action,
            "ProductIdentity",
            objectId,
            result,
            reasonCode,
            correlationId));

    private static TestRunView ToView(TestRun run, string serialNumber, bool isReplay) => new(
        run.Id,
        run.ProductIdentityId,
        serialNumber,
        run.ProductionOrderId,
        run.ExecutionSnapshotId,
        run.SpecificationCode,
        run.SpecificationVersion,
        run.SpecificationDefinitionHash,
        run.OperationCode,
        run.DeviceId,
        run.DeviceVersion,
        run.FixtureId,
        run.FixtureVersion,
        run.RawReportReference,
        run.Result.ToString(),
        run.DiagnosticCode,
        run.DiagnosticMessage,
        run.RetryOfTestRunId,
        run.ManufacturingEventId,
        run.ActorUserId,
        run.ActorUsername,
        run.StartedAtUtc,
        run.EndedAtUtc,
        run.RecordedAtUtc,
        run.Location,
        run.CorrelationId,
        run.OperationCompleted,
        run.NextOperationCodeAfter,
        isReplay,
        run.Measurements.OrderBy(item => item.ItemCode, StringComparer.Ordinal).Select(ToView).ToArray());

    private static TestMeasurementView ToView(TestMeasurement measurement) => new(
        measurement.Id,
        measurement.ItemCode,
        measurement.ItemName,
        measurement.DataType,
        measurement.Required,
        measurement.RawValue,
        measurement.Unit,
        measurement.DecimalPlaces,
        measurement.LowerLimit,
        measurement.UpperLimit,
        measurement.ExpectedText,
        measurement.ExpectedBoolean,
        measurement.NumericValue,
        measurement.BooleanValue,
        measurement.Result.ToString(),
        measurement.DiagnosticCode,
        measurement.DiagnosticMessage,
        measurement.ReportedDiagnosticCode,
        measurement.ReportedDiagnosticMessage);

    private static ParsedCommand Parse(TestRunRequest request, string serialNumber)
    {
        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var specificationCode = Normalize(request.SpecificationCode);
        var deviceId = Normalize(request.DeviceId);
        var deviceVersion = Normalize(request.DeviceVersion);
        var fixtureId = Normalize(request.FixtureId);
        var fixtureVersion = Normalize(request.FixtureVersion);
        var rawReportReference = Normalize(request.RawReportReference);
        var location = Normalize(request.Location);
        var measurements = request.Measurements ?? [];
        if (!Required(sourceSystem, 80)
            || !Required(idempotencyKey, 120)
            || !Required(specificationCode, 80)
            || !Required(deviceId, 120)
            || !Required(deviceVersion, 80)
            || !Required(fixtureId, 120)
            || !Required(fixtureVersion, 80)
            || !Required(rawReportReference, 400)
            || !Required(location, 120)
            || measurements.Count == 0
            || request.StartedAtUtc == default
            || request.EndedAtUtc < request.StartedAtUtc)
        {
            throw Rejected("TEST_RUN_INVALID", "测试执行字段不完整或时间范围无效，请核对设备、夹具、报告和采集时间。", 422);
        }

        var normalizedMeasurements = measurements.Select(item => item with
        {
            ItemCode = Normalize(item.ItemCode),
            RawValue = item.RawValue,
            Unit = NullIfEmpty(item.Unit),
            DiagnosticCode = NullIfEmpty(item.DiagnosticCode),
            DiagnosticMessage = NullIfEmpty(item.DiagnosticMessage),
        }).ToArray();
        if (normalizedMeasurements.Any(item => item.ItemCode!.Length > 80
            || (item.DiagnosticCode?.Length ?? 0) > 80
            || (item.DiagnosticMessage?.Length ?? 0) > 1000))
        {
            throw Rejected("TEST_RUN_INVALID", "测试项目编码或诊断信息超过允许长度，请核对设备映射。", 422);
        }

        var startedAtUtc = request.StartedAtUtc.ToUniversalTime();
        var endedAtUtc = request.EndedAtUtc.ToUniversalTime();
        var hash = Hash(new
        {
            serialNumber,
            sourceSystem,
            idempotencyKey,
            specificationCode,
            deviceId,
            deviceVersion,
            fixtureId,
            fixtureVersion,
            rawReportReference,
            request.RetryOfTestRunId,
            startedAtUtc,
            endedAtUtc,
            location,
            measurements = normalizedMeasurements.OrderBy(item => item.ItemCode, StringComparer.Ordinal),
        });
        return new ParsedCommand(
            sourceSystem,
            idempotencyKey,
            specificationCode,
            deviceId,
            deviceVersion,
            fixtureId,
            fixtureVersion,
            rawReportReference,
            request.RetryOfTestRunId,
            startedAtUtc,
            endedAtUtc,
            location,
            normalizedMeasurements,
            hash);
    }

    private static ExecutionTemplateDefinition DeserializeDefinition(string json) =>
        JsonSerializer.Deserialize<ExecutionTemplateDefinition>(json, WebJson)
        ?? throw Rejected("EXECUTION_SNAPSHOT_INVALID", "订单执行快照无法解析，请停止生产并联系系统管理员。", 500);

    private static string? NextOperationCode(
        ExecutionTemplateDefinition definition,
        string currentOperationCode)
    {
        var operations = definition.Route.Operations.OrderBy(item => item.Sequence).ToArray();
        var index = Array.FindIndex(operations, item => item.Code == currentOperationCode);
        return index >= 0 && index + 1 < operations.Length ? operations[index + 1].Code : null;
    }

    private static string Hash(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WebJson))));

    private static bool Required(string value, int maxLength) =>
        value.Length is > 0 && value.Length <= maxLength;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TestRunRejectedException Rejected(string code, string message, int statusCode) =>
        new(code, message, statusCode);

    private sealed record ParsedCommand(
        string SourceSystem,
        string IdempotencyKey,
        string SpecificationCode,
        string DeviceId,
        string DeviceVersion,
        string FixtureId,
        string FixtureVersion,
        string RawReportReference,
        Guid? RetryOfTestRunId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        string Location,
        IReadOnlyList<TestMeasurementRequest> Measurements,
        string CommandHash);

    private sealed record EvaluatedMeasurement(
        TestSpecificationItemDefinition Item,
        TestMeasurementRequest Request,
        decimal? NumericValue,
        bool? BooleanValue,
        TestMeasurementResult Result,
        string? DiagnosticCode,
        string? DiagnosticMessage);
}
