using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

/// <summary>
/// 记录固件下载/配置执行结果，并将实际版本、配置摘要和设备证据纳入产品谱系。
/// 执行规则来自订单释放快照，避免模板升级影响在制品。
/// </summary>
public sealed class FirmwareConfigurationService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string ExecuteAction = "FIRMWARE_CONFIGURATION_EXECUTE";
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<FirmwareExecutionResultView> ExecuteAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        FirmwareExecutionRequest request,
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
            // 回执提供幂等语义：相同命令可安全重试，不同内容复用同一键则按冲突拒绝。
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var replay = await context.FirmwareConfigurationExecutions
                    .AsNoTracking()
                    .Include(item => item.ProductIdentity)
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == command.SourceSystem
                            && item.IdempotencyKey == command.IdempotencyKey,
                        cancellationToken);
                if (replay is not null)
                {
                    if (!string.Equals(replay.CommandHash, command.CommandHash, StringComparison.Ordinal))
                    {
                        throw Rejected(
                            "FIRMWARE_IDEMPOTENCY_CONFLICT",
                            "相同来源和幂等键已用于不同固件执行请求，请核对原始命令。",
                            409);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return ToView(replay, replay.ProductIdentity!.SerialNumber, true);
                }

                var identity = await context.ProductIdentities
                    .Include(item => item.ProductionOrder)
                    .Include(item => item.ExecutionSnapshot)
                    .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken)
                    ?? throw Rejected("PRODUCT_IDENTITY_NOT_FOUND", "未找到成品 SN。", 404);
                if (identity.Status != ProductIdentityStatus.Bound
                    || identity.ProductionOrder is null
                    || identity.ExecutionSnapshot is null
                    || identity.ProductionOrderId is null
                    || identity.ExecutionSnapshotId is null)
                {
                    throw Rejected(
                        "FIRMWARE_PRODUCT_NOT_IN_WIP",
                        "该成品尚未投入生产，不能记录固件配置。",
                        409);
                }

                if (identity.ProductionOrder.Status != ProductionOrderStatus.InProduction)
                {
                    throw Rejected(
                        "FIRMWARE_ORDER_STATUS_BLOCKED",
                        "生产订单当前不是执行中状态，不能记录固件配置。",
                        409);
                }

                var definition = DeserializeDefinition(identity.ExecutionSnapshot.DefinitionJson);
                var requirement = definition.FirmwareRequirements.SingleOrDefault(item =>
                    string.Equals(item.Code, command.RequirementCode, StringComparison.Ordinal))
                    ?? throw Rejected(
                        "FIRMWARE_REQUIREMENT_NOT_FOUND",
                        "订单冻结快照不包含该固件要求。",
                        422);
                var snapshot = ParseSnapshotRequirement(requirement, definition);
                if (!string.Equals(identity.NextOperationCode, snapshot.OperationCode, StringComparison.Ordinal))
                {
                    throw Rejected(
                        "FIRMWARE_OPERATION_MISMATCH",
                        $"成品当前应执行工序为 {identity.NextOperationCode ?? "<none>"}，不能记录固件配置。",
                        409);
                }

                if (await context.FirmwareConfigurationExecutions.AnyAsync(
                        item => item.ProductIdentityId == identity.Id
                            && item.RequirementCode == requirement.Code
                            && item.Result == FirmwareExecutionResult.Succeeded,
                        cancellationToken))
                {
                    throw Rejected(
                        "FIRMWARE_REQUIREMENT_ALREADY_SUCCEEDED",
                        "该成品的固件要求已经成功完成，不能重复生成成功事实。",
                        409);
                }

                FirmwareConfigurationExecution? retryOf = null;
                var latestFailure = await context.FirmwareConfigurationExecutions
                    .AsNoTracking()
                    .Where(item => item.ProductIdentityId == identity.Id
                        && item.RequirementCode == requirement.Code
                        && item.Result == FirmwareExecutionResult.Failed)
                    .OrderByDescending(item => item.RecordedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latestFailure is not null && command.RetryOfExecutionId is null)
                {
                    throw Rejected(
                        "FIRMWARE_RETRY_REFERENCE_REQUIRED",
                        "该固件要求已有失败执行，重试必须引用最新失败记录。",
                        409);
                }

                if (command.RetryOfExecutionId is not null)
                {
                    retryOf = await context.FirmwareConfigurationExecutions
                        .SingleOrDefaultAsync(
                            item => item.Id == command.RetryOfExecutionId
                                && item.ProductIdentityId == identity.Id
                                && item.RequirementCode == requirement.Code,
                            cancellationToken)
                        ?? throw Rejected(
                            "FIRMWARE_RETRY_REFERENCE_INVALID",
                            "重试引用不存在或不属于当前成品和固件要求。",
                            422);
                    if (retryOf.Result != FirmwareExecutionResult.Failed)
                    {
                        throw Rejected(
                            "FIRMWARE_RETRY_REFERENCE_INVALID",
                            "重试只能引用原失败执行。",
                            422);
                    }

                    if (latestFailure is not null && retryOf.Id != latestFailure.Id)
                    {
                        throw Rejected(
                            "FIRMWARE_RETRY_REFERENCE_STALE",
                            "重试必须引用该固件要求的最新失败执行。",
                            409);
                    }
                }

                var evidenceMatches = string.Equals(
                        command.ActualVersion,
                        snapshot.RequiredVersion,
                        StringComparison.Ordinal)
                    && string.Equals(
                        command.ConfigurationPackage,
                        snapshot.ConfigurationPackage,
                        StringComparison.Ordinal)
                    && string.Equals(
                        command.ChecksumAlgorithm,
                        snapshot.ChecksumAlgorithm,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        command.ChecksumValue,
                        snapshot.ExpectedChecksum,
                        StringComparison.OrdinalIgnoreCase);
                var result = command.ReportedResult == FirmwareExecutionResult.Succeeded && evidenceMatches
                    ? FirmwareExecutionResult.Succeeded
                    : FirmwareExecutionResult.Failed;
                var diagnosticCode = result == FirmwareExecutionResult.Failed
                    ? evidenceMatches ? command.DiagnosticCode : "FIRMWARE_EVIDENCE_MISMATCH"
                    : null;
                var diagnosticMessage = result == FirmwareExecutionResult.Failed
                    ? evidenceMatches
                        ? command.DiagnosticMessage
                        : "实际版本、配置包或校验值与订单冻结要求不一致。"
                    : null;
                if (result == FirmwareExecutionResult.Failed
                    && (string.IsNullOrWhiteSpace(diagnosticCode)
                        || string.IsNullOrWhiteSpace(diagnosticMessage)))
                {
                    throw Rejected(
                        "FIRMWARE_FAILURE_DIAGNOSTIC_REQUIRED",
                        "失败执行必须提供诊断代码和诊断信息。",
                        422);
                }

                var now = timeProvider.GetUtcNow();
                var executionEvent = AppendEvent(
                    "FIRMWARE_CONFIGURATION_EXECUTED",
                    identity,
                    actor,
                    command.EndedAtUtc,
                    now,
                    command.Location,
                    correlationId,
                    new
                    {
                        requirementCode = requirement.Code,
                        operationCode = snapshot.OperationCode,
                        requiredVersion = snapshot.RequiredVersion,
                        actualVersion = command.ActualVersion,
                        configurationPackage = command.ConfigurationPackage,
                        checksumAlgorithm = command.ChecksumAlgorithm,
                        checksumValue = command.ChecksumValue,
                        toolId = command.ToolId,
                        toolVersion = command.ToolVersion,
                        result = result.ToString(),
                        diagnosticCode,
                        diagnosticMessage,
                        reportedDiagnosticCode = command.DiagnosticCode,
                        reportedDiagnosticMessage = command.DiagnosticMessage,
                        retryOfExecutionId = retryOf?.Id,
                    },
                    retryOf?.ManufacturingEventId);
                var operationCompleted = false;
                if (result == FirmwareExecutionResult.Succeeded)
                {
                    var otherRequiredCodes = definition.FirmwareRequirements
                        .Where(item => item.Required
                            && string.Equals(
                                item.OperationCode,
                                snapshot.OperationCode,
                                StringComparison.Ordinal)
                            && !string.Equals(item.Code, requirement.Code, StringComparison.Ordinal))
                        .Select(item => item.Code)
                        .ToArray();
                    var completedCodes = await context.FirmwareConfigurationExecutions
                        .Where(item => item.ProductIdentityId == identity.Id
                            && item.Result == FirmwareExecutionResult.Succeeded
                            && otherRequiredCodes.Contains(item.RequirementCode))
                        .Select(item => item.RequirementCode)
                        .Distinct()
                        .ToArrayAsync(cancellationToken);
                    operationCompleted = completedCodes.Length == otherRequiredCodes.Length;
                    if (operationCompleted)
                    {
                        identity.NextOperationCode = NextOperationCode(definition, snapshot.OperationCode);
                        AppendEvent(
                            "FIRMWARE_CONFIGURATION_COMPLETED",
                            identity,
                            actor,
                            command.EndedAtUtc,
                            now,
                            command.Location,
                            correlationId,
                            new
                            {
                                operationCode = snapshot.OperationCode,
                                nextOperationCode = identity.NextOperationCode,
                            },
                            executionEvent.Id);
                    }
                }

                var execution = new FirmwareConfigurationExecution
                {
                    Id = Guid.NewGuid(),
                    ProductIdentityId = identity.Id,
                    ProductionOrderId = identity.ProductionOrderId.Value,
                    ExecutionSnapshotId = identity.ExecutionSnapshotId.Value,
                    RequirementCode = requirement.Code,
                    OperationCode = snapshot.OperationCode,
                    RequiredVersion = snapshot.RequiredVersion,
                    ActualVersion = command.ActualVersion,
                    RequiredConfigurationPackage = snapshot.ConfigurationPackage,
                    ActualConfigurationPackage = command.ConfigurationPackage,
                    RequiredChecksumAlgorithm = snapshot.ChecksumAlgorithm,
                    ActualChecksumAlgorithm = command.ChecksumAlgorithm,
                    ExpectedChecksum = snapshot.ExpectedChecksum,
                    ActualChecksum = command.ChecksumValue,
                    ToolId = command.ToolId,
                    ToolVersion = command.ToolVersion,
                    Result = result,
                    OperationCompleted = operationCompleted,
                    NextOperationCodeAfter = identity.NextOperationCode,
                    DiagnosticCode = diagnosticCode,
                    DiagnosticMessage = diagnosticMessage,
                    ReportedDiagnosticCode = NullIfEmpty(command.DiagnosticCode),
                    ReportedDiagnosticMessage = NullIfEmpty(command.DiagnosticMessage),
                    RetryOfExecutionId = retryOf?.Id,
                    ManufacturingEventId = executionEvent.Id,
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
                };
                context.FirmwareConfigurationExecutions.Add(execution);
                AppendAudit(actor, ExecuteAction, identity.Id.ToString(), BusinessAuditResult.Succeeded, null, correlationId);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToView(execution, identity.SerialNumber, false);
            });
        }
        catch (FirmwareConfigurationRejectedException error)
        {
            context.ChangeTracker.Clear();
            AppendAudit(actor, ExecuteAction, serialNumber, BusinessAuditResult.Denied, error.Code, correlationId);
            await context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<FirmwareWorkstationResult> ReadWorkstationAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadIdentityAsync(
            actor,
            BusinessCapability.StationExecute,
            "FIRMWARE_WORKSTATION_READ",
            finishedSerialNumber,
            correlationId,
            cancellationToken);
        var definition = DeserializeDefinition(identity.ExecutionSnapshot!.DefinitionJson);
        var executions = await ReadExecutionsAsync(identity.Id, cancellationToken);
        var requirements = definition.FirmwareRequirements.Select(requirement =>
        {
            var snapshot = ParseSnapshotRequirement(requirement, definition);
            var history = executions.Where(item => item.RequirementCode == requirement.Code).ToArray();
            return new FirmwareRequirementView(
                requirement.Code,
                snapshot.OperationCode,
                snapshot.RequiredVersion,
                snapshot.ConfigurationPackage,
                snapshot.ChecksumAlgorithm,
                snapshot.ExpectedChecksum,
                requirement.Required,
                history.Any(item => item.Result == "Succeeded")
                    ? "Succeeded"
                    : history.Length > 0 ? "Failed" : "Pending",
                history);
        }).ToArray();
        return new FirmwareWorkstationResult(
            identity.Id,
            identity.SerialNumber,
            identity.NextOperationCode,
            requirements);
    }

    public async Task<FirmwareGenealogyResult> ReadGenealogyAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadIdentityAsync(
            actor,
            BusinessCapability.GenealogyRead,
            "FIRMWARE_GENEALOGY_READ",
            finishedSerialNumber,
            correlationId,
            cancellationToken);
        return new FirmwareGenealogyResult(
            identity.Id,
            identity.SerialNumber,
            identity.ProductionOrderId!.Value,
            await ReadExecutionsAsync(identity.Id, cancellationToken));
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
            .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken)
            is { Status: ProductIdentityStatus.Bound, ExecutionSnapshot: not null } identity
                ? identity
                : throw Rejected("FIRMWARE_PRODUCT_NOT_IN_WIP", "未找到已投产成品 SN。", 404);
    }

    private async Task<IReadOnlyList<FirmwareExecutionResultView>> ReadExecutionsAsync(
        Guid productIdentityId,
        CancellationToken cancellationToken) => await context.FirmwareConfigurationExecutions
        .AsNoTracking()
        .Include(item => item.ProductIdentity)
        .Where(item => item.ProductIdentityId == productIdentityId)
        .OrderBy(item => item.RecordedAtUtc)
        .Select(item => ToView(item, item.ProductIdentity!.SerialNumber, false))
        .ToArrayAsync(cancellationToken);

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

    private static FirmwareExecutionResultView ToView(
        FirmwareConfigurationExecution execution,
        string serialNumber,
        bool isReplay) => new(
            execution.Id,
            execution.ProductIdentityId,
            serialNumber,
            execution.RequirementCode,
            execution.RequiredVersion,
            execution.ActualVersion,
            execution.RequiredConfigurationPackage,
            execution.ActualConfigurationPackage,
            execution.RequiredChecksumAlgorithm,
            execution.ActualChecksumAlgorithm,
            execution.ExpectedChecksum,
            execution.ActualChecksum,
            execution.ToolId,
            execution.ToolVersion,
            execution.Result.ToString(),
            execution.DiagnosticCode,
            execution.DiagnosticMessage,
            execution.ReportedDiagnosticCode,
            execution.ReportedDiagnosticMessage,
            execution.RetryOfExecutionId,
            execution.ProductionOrderId,
            execution.ExecutionSnapshotId,
            execution.OperationCode,
            execution.ManufacturingEventId,
            execution.ActorUserId,
            execution.ActorUsername,
            execution.StartedAtUtc,
            execution.EndedAtUtc,
            execution.RecordedAtUtc,
            execution.Location,
            execution.CorrelationId,
            execution.OperationCompleted,
            execution.NextOperationCodeAfter,
            isReplay);

    private static SnapshotFirmwareRequirement ParseSnapshotRequirement(
        FirmwareRequirementDefinition requirement,
        ExecutionTemplateDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(requirement.OperationCode)
            || string.IsNullOrWhiteSpace(requirement.ConfigurationPackage)
            || string.IsNullOrWhiteSpace(requirement.ChecksumAlgorithm)
            || string.IsNullOrWhiteSpace(requirement.ExpectedChecksum)
            || !definition.Route.Operations.Any(item => item.Code == requirement.OperationCode))
        {
            throw Rejected(
                "FIRMWARE_SNAPSHOT_REQUIREMENT_INVALID",
                "订单执行快照中的固件要求缺少工序、配置包或校验依据，不能补造默认值。",
                500);
        }

        return new SnapshotFirmwareRequirement(
            requirement.OperationCode,
            requirement.Version,
            requirement.ConfigurationPackage,
            requirement.ChecksumAlgorithm,
            requirement.ExpectedChecksum);
    }

    private static ExecutionTemplateDefinition DeserializeDefinition(string json) =>
        JsonSerializer.Deserialize<ExecutionTemplateDefinition>(json, WebJson)
        ?? throw Rejected("EXECUTION_SNAPSHOT_INVALID", "订单执行快照无法解析。", 500);

    private static string? NextOperationCode(
        ExecutionTemplateDefinition definition,
        string currentOperationCode)
    {
        var operations = definition.Route.Operations.OrderBy(item => item.Sequence).ToArray();
        var index = Array.FindIndex(operations, item => item.Code == currentOperationCode);
        return index >= 0 && index + 1 < operations.Length ? operations[index + 1].Code : null;
    }

    private static ParsedCommand Parse(FirmwareExecutionRequest request, string serialNumber)
    {
        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var requirementCode = Normalize(request.RequirementCode);
        var actualVersion = Normalize(request.ActualVersion);
        var configurationPackage = Normalize(request.ConfigurationPackage);
        var checksumAlgorithm = Normalize(request.ChecksumAlgorithm);
        var checksumValue = Normalize(request.ChecksumValue);
        var toolId = Normalize(request.ToolId);
        var toolVersion = Normalize(request.ToolVersion);
        var location = Normalize(request.Location);
        var diagnosticCode = Normalize(request.DiagnosticCode);
        var diagnosticMessage = Normalize(request.DiagnosticMessage);
        if (!Enum.TryParse<FirmwareExecutionResult>(request.Result, false, out var result)
            || !Required(sourceSystem, 80)
            || !Required(idempotencyKey, 120)
            || !Required(requirementCode, 80)
            || !Required(actualVersion, 120)
            || !Required(configurationPackage, 160)
            || !Required(checksumAlgorithm, 40)
            || !Required(checksumValue, 256)
            || !Required(toolId, 120)
            || !Required(toolVersion, 80)
            || !Required(location, 120)
            || diagnosticCode.Length > 80
            || diagnosticMessage.Length > 1000
            || request.StartedAtUtc == default
            || request.EndedAtUtc < request.StartedAtUtc)
        {
            throw Rejected("FIRMWARE_EXECUTION_INVALID", "固件执行请求字段不完整或时间范围无效。", 422);
        }

        var startedAtUtc = request.StartedAtUtc.ToUniversalTime();
        var endedAtUtc = request.EndedAtUtc.ToUniversalTime();
        var hash = Hash(new
        {
            serialNumber,
            sourceSystem,
            idempotencyKey,
            requirementCode,
            actualVersion,
            configurationPackage,
            checksumAlgorithm,
            checksumValue,
            toolId,
            toolVersion,
            result,
            diagnosticCode,
            diagnosticMessage,
            request.RetryOfExecutionId,
            startedAtUtc,
            endedAtUtc,
            location,
        });
        return new ParsedCommand(
            sourceSystem,
            idempotencyKey,
            requirementCode,
            actualVersion,
            configurationPackage,
            checksumAlgorithm,
            checksumValue,
            toolId,
            toolVersion,
            result,
            diagnosticCode,
            diagnosticMessage,
            request.RetryOfExecutionId,
            startedAtUtc,
            endedAtUtc,
            location,
            hash);
    }

    private static string Hash(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WebJson))));

    private static bool Required(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static FirmwareConfigurationRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record SnapshotFirmwareRequirement(
        string OperationCode,
        string RequiredVersion,
        string ConfigurationPackage,
        string ChecksumAlgorithm,
        string ExpectedChecksum);

    private sealed record ParsedCommand(
        string SourceSystem,
        string IdempotencyKey,
        string RequirementCode,
        string ActualVersion,
        string ConfigurationPackage,
        string ChecksumAlgorithm,
        string ChecksumValue,
        string ToolId,
        string ToolVersion,
        FirmwareExecutionResult ReportedResult,
        string DiagnosticCode,
        string DiagnosticMessage,
        Guid? RetryOfExecutionId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        string Location,
        string CommandHash);
}
