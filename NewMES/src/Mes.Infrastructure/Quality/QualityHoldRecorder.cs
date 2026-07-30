using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Quality;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Quality;

/// <summary>
/// 在现有业务事务中追加不合格与质量保留。记录器不自行提交，调用方必须把原失败、质量事实、
/// 订单保留计数、制造事件和业务审计作为一个原子事实包保存。
/// </summary>
internal sealed class QualityHoldRecorder(
    MesDbContext context,
    TimeProvider timeProvider)
{
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<NonconformanceRecord> RecordTestFailureAsync(
        ProductIdentity identity,
        TestRun run,
        EffectiveIdentity actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var sourceSystem = "MES-TEST-EXECUTION";
        var idempotencyKey = $"TEST-RUN:{run.Id:N}";
        var commandJson = JsonSerializer.Serialize(new
        {
            productIdentityId = identity.Id,
            testRunId = run.Id,
            run.OperationCode,
            run.DiagnosticCode,
            run.DiagnosticMessage,
            run.RawReportReference,
        }, WebJson);
        var result = await RecordAsync(
            identity,
            new QualityOccurrence(
                sourceSystem,
                idempotencyKey,
                Hash(commandJson),
                run.OperationCode,
                run.DiagnosticCode ?? "TEST_FAILED",
                run.DiagnosticMessage ?? "测试执行未通过。",
                run.RawReportReference,
                run.Id,
                run.EndedAtUtc,
                run.Location,
                run.ManufacturingEventId),
            actor,
            correlationId,
            cancellationToken);
        return result.Nonconformance;
    }

    /// <summary>
    /// 追加任一来源的不合格事实，并复用产品已有的有效保留。调用方仍负责外围事务与最终提交。
    /// </summary>
    public async Task<QualityRecordingResult> RecordAsync(
        ProductIdentity identity,
        QualityOccurrence occurrence,
        EffectiveIdentity actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var replay = await ReplayAsync(
            occurrence.SourceSystem,
            occurrence.IdempotencyKey,
            occurrence.CommandHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var now = timeProvider.GetUtcNow();
        var hold = await context.QualityHolds.SingleOrDefaultAsync(
            item => item.ProductIdentityId == identity.Id
                && item.Status == QualityHoldStatus.Active,
            cancellationToken);
        var nonconformanceId = Guid.NewGuid();
        var nonconformanceEvent = AppendEvent(
            "NONCONFORMANCE_RECORDED",
            identity,
            actor,
            occurrence.DetectedAtUtc,
            now,
            occurrence.Location,
            correlationId,
            new
            {
                nonconformanceId,
                relatedTestRunId = occurrence.RelatedTestRunId,
                operationCode = occurrence.DetectedOperationCode,
                occurrence.DefectCode,
                occurrence.Phenomenon,
                occurrence.EvidenceReference,
            },
            occurrence.CausationEventId);

        if (hold is null)
        {
            var holdId = Guid.NewGuid();
            var holdEvent = AppendEvent(
                "QUALITY_HOLD_STARTED",
                identity,
                actor,
                occurrence.DetectedAtUtc,
                now,
                occurrence.Location,
                correlationId,
                new
                {
                    holdId,
                    startReasonCode = "OPEN_NONCONFORMANCE",
                    releaseCondition = "APPROVED_DISPOSITION_REQUIRED",
                    nonconformanceId,
                },
                nonconformanceEvent.Id);
            hold = new QualityHold
            {
                Id = holdId,
                ProductIdentityId = identity.Id,
                ProductionOrderId = identity.ProductionOrderId!.Value,
                Status = QualityHoldStatus.Active,
                StartReasonCode = "OPEN_NONCONFORMANCE",
                StartReason = "产品存在待处置不合格，暂停正常制造流转。",
                ReleaseCondition = "必须完成授权质量处置及其要求的验证后方可解除。",
                StartedAtUtc = now,
                StartedByUserId = actor.UserId,
                StartedByUsername = actor.Username,
                ManufacturingEventId = holdEvent.Id,
                CorrelationId = correlationId,
            };
            context.QualityHolds.Add(hold);

            // 订单计数按“被保留产品数”累计，而不是按不合格条数累计；同一 SN 的后续异常复用保留。
            identity.ProductionOrder!.OpenQualityHoldQuantity += 1;
            AppendAudit(
                actor,
                BusinessCapability.DefectReport,
                "QUALITY_HOLD_START",
                "QualityHold",
                hold.Id.ToString(),
                correlationId);
        }

        var nonconformance = new NonconformanceRecord
        {
            Id = nonconformanceId,
            ReferenceNumber = $"NC-{nonconformanceId:N}".ToUpperInvariant(),
            ProductIdentityId = identity.Id,
            ProductionOrderId = identity.ProductionOrderId!.Value,
            ExecutionSnapshotId = identity.ExecutionSnapshotId!.Value,
            QualityHoldId = hold.Id,
            Status = NonconformanceStatus.Open,
            DetectedOperationCode = occurrence.DetectedOperationCode,
            DefectCode = occurrence.DefectCode,
            Phenomenon = occurrence.Phenomenon,
            EvidenceReference = occurrence.EvidenceReference,
            RelatedTestRunId = occurrence.RelatedTestRunId,
            SourceSystem = occurrence.SourceSystem,
            IdempotencyKey = occurrence.IdempotencyKey,
            CommandHash = occurrence.CommandHash,
            CommandHashAlgorithm = HashAlgorithm,
            ReportedByUserId = actor.UserId,
            ReportedByUsername = actor.Username,
            DetectedAtUtc = occurrence.DetectedAtUtc,
            RecordedAtUtc = now,
            Location = occurrence.Location,
            ManufacturingEventId = nonconformanceEvent.Id,
            CorrelationId = correlationId,
        };
        context.NonconformanceRecords.Add(nonconformance);
        AppendAudit(
            actor,
            BusinessCapability.DefectReport,
            "NONCONFORMANCE_RECORD",
            "NonconformanceRecord",
            nonconformance.Id.ToString(),
            correlationId);
        return new QualityRecordingResult(nonconformance, hold, false);
    }

    /// <summary>在读取产品当前状态前解析永久幂等回执，避免后续状态变化破坏原命令重放。</summary>
    public async Task<QualityRecordingResult?> ReplayAsync(
        string sourceSystem,
        string idempotencyKey,
        string commandHash,
        CancellationToken cancellationToken)
    {
        var existing = await context.NonconformanceRecords
            .Include(item => item.ProductIdentity)
            .Include(item => item.QualityHold)
            .SingleOrDefaultAsync(
                item => item.SourceSystem == sourceSystem
                    && item.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.CommandHash, commandHash, StringComparison.Ordinal)
            || !string.Equals(existing.CommandHashAlgorithm, HashAlgorithm, StringComparison.Ordinal))
        {
            throw new NonconformanceRejectedException(
                "NONCONFORMANCE_IDEMPOTENCY_CONFLICT",
                "相同来源系统和幂等键已用于另一条不合格报告，请核对原始命令。",
                409);
        }

        return new QualityRecordingResult(existing, existing.QualityHold!, true);
    }

    public static string Hash(string commandJson) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(commandJson)));

    private ManufacturingEvent AppendEvent(
        string eventType,
        ProductIdentity identity,
        EffectiveIdentity actor,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string location,
        string correlationId,
        object payload,
        Guid? causationEventId)
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
        BusinessCapability capability,
        string action,
        string objectType,
        string objectId,
        string correlationId)
    {
        var grant = RoleCapabilityMatrix.Authorize(actor.Roles, capability);
        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            grant.GrantedByRole,
            capability,
            action,
            objectType,
            objectId,
            BusinessAuditResult.Succeeded,
            null,
            correlationId));
    }
}

/// <summary>已规范化的不合格发生事实；哈希必须覆盖这些业务字段以保护幂等键语义。</summary>
internal sealed record QualityOccurrence(
    string SourceSystem,
    string IdempotencyKey,
    string CommandHash,
    string DetectedOperationCode,
    string DefectCode,
    string Phenomenon,
    string EvidenceReference,
    Guid? RelatedTestRunId,
    DateTimeOffset DetectedAtUtc,
    string Location,
    Guid? CausationEventId);

internal sealed record QualityRecordingResult(
    NonconformanceRecord Nonconformance,
    QualityHold Hold,
    bool IsReplay);
