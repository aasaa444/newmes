using System.Data;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Quality;

/// <summary>
/// 接收操作工现场异常报告。该服务只建立不合格和质量保留，不接受处置、放行或解除保留意见。
/// </summary>
public sealed class NonconformanceService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly QualityHoldRecorder recorder = new(context, timeProvider);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<NonconformanceReportResult> ReportAsync(
        EffectiveIdentity actor,
        NonconformanceReportRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestedSerialNumber = Normalize(request.FinishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.DefectReport,
            "NONCONFORMANCE_REPORT",
            "ProductIdentity",
            requestedSerialNumber,
            correlationId,
            cancellationToken);

        try
        {
            var command = Parse(request);
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                // 永久回执先于产品、订单和当前工序判断；重放不能被后续处置或路线推进改变语义。
                var replay = await recorder.ReplayAsync(
                    command.SourceSystem,
                    command.IdempotencyKey,
                    command.CommandHash,
                    cancellationToken);
                if (replay is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ToResult(replay, replay.Nonconformance.ProductIdentity!.SerialNumber);
                }

                var identity = await context.ProductIdentities
                    .Include(item => item.ProductionOrder)
                    .Include(item => item.ExecutionSnapshot)
                    .SingleOrDefaultAsync(
                        item => item.SerialNumber == command.FinishedSerialNumber,
                        cancellationToken)
                    ?? throw Rejected(
                        "PRODUCT_IDENTITY_NOT_FOUND",
                        "未找到成品 SN，请核对扫描内容和投产记录。",
                        404);
                if (identity.Status != ProductIdentityStatus.Bound
                    || identity.ProductionOrder is null
                    || identity.ExecutionSnapshot is null
                    || identity.ProductionOrderId is null
                    || identity.ExecutionSnapshotId is null)
                {
                    throw Rejected(
                        "NONCONFORMANCE_PRODUCT_NOT_IN_WIP",
                        "该成品尚未绑定生产订单执行快照，不能报告工位异常。",
                        409);
                }

                // 现场操作工只能报告当前执行工序观察到的异常，不能借此命令补录或改写其他工序事实。
                if (!string.Equals(
                        identity.NextOperationCode,
                        command.DetectedOperationCode,
                        StringComparison.Ordinal))
                {
                    throw Rejected(
                        "NONCONFORMANCE_OPERATION_MISMATCH",
                        $"成品当前工序为 {identity.NextOperationCode ?? "<无>"}，不能报告 {command.DetectedOperationCode} 工序异常。",
                        409);
                }

                var recording = await recorder.RecordAsync(
                    identity,
                    new QualityOccurrence(
                        command.SourceSystem,
                        command.IdempotencyKey,
                        command.CommandHash,
                        command.DetectedOperationCode,
                        command.DefectCode,
                        command.Phenomenon,
                        command.EvidenceReference,
                        null,
                        command.DetectedAtUtc,
                        command.Location,
                        null),
                    actor,
                    correlationId,
                    cancellationToken);

                if (!recording.IsReplay)
                {
                    // 新不合格、可能的新保留、订单保留计数、制造事件及审计必须作为一个事实包落库。
                    await context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return ToResult(recording, identity.SerialNumber);
            });
        }
        catch (NonconformanceRejectedException error)
        {
            // 拒绝审计独立于已回滚的制造事务提交，证明系统没有静默丢弃越权或非法尝试。
            context.ChangeTracker.Clear();
            var grant = RoleCapabilityMatrix.Authorize(actor.Roles, BusinessCapability.DefectReport);
            auditWriter.Append(new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                grant.GrantedByRole,
                BusinessCapability.DefectReport,
                "NONCONFORMANCE_REPORT",
                "ProductIdentity",
                requestedSerialNumber,
                BusinessAuditResult.Denied,
                error.Code,
                correlationId));
            await context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static ParsedReport Parse(NonconformanceReportRequest request)
    {
        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var serialNumber = Normalize(request.FinishedSerialNumber);
        var operationCode = Normalize(request.DetectedOperationCode);
        var defectCode = Normalize(request.DefectCode);
        var phenomenon = Normalize(request.Phenomenon);
        var evidenceReference = Normalize(request.EvidenceReference);
        var location = Normalize(request.Location);
        if (!Required(sourceSystem, 80)
            || !Required(idempotencyKey, 120)
            || !Required(serialNumber, 120)
            || !Required(operationCode, 80)
            || !Required(defectCode, 80)
            || !Required(phenomenon, 1000)
            || !Required(evidenceReference, 400)
            || !Required(location, 120)
            || request.DetectedAtUtc is null
            || request.DetectedAtUtc == default)
        {
            throw Rejected(
                "NONCONFORMANCE_REPORT_INVALID",
                "异常报告字段不完整或超过允许长度，请核对来源、SN、工序、缺陷、现象、证据、时间和位置。",
                422);
        }

        var detectedAtUtc = request.DetectedAtUtc.Value.ToUniversalTime();
        // 先规范化再计算哈希，使仅有首尾空格或时区表示差异的重试仍被识别为同一业务命令。
        var commandJson = JsonSerializer.Serialize(new
        {
            sourceSystem,
            idempotencyKey,
            finishedSerialNumber = serialNumber,
            detectedOperationCode = operationCode,
            defectCode,
            phenomenon,
            evidenceReference,
            detectedAtUtc,
            location,
        }, WebJson);
        return new ParsedReport(
            sourceSystem,
            idempotencyKey,
            serialNumber,
            operationCode,
            defectCode,
            phenomenon,
            evidenceReference,
            detectedAtUtc,
            location,
            QualityHoldRecorder.Hash(commandJson));
    }

    private static NonconformanceReportResult ToResult(
        QualityRecordingResult recording,
        string serialNumber) => new(
            recording.Nonconformance.Id,
            recording.Nonconformance.ReferenceNumber,
            recording.Nonconformance.Status.ToString(),
            recording.Nonconformance.ProductIdentityId,
            serialNumber,
            recording.Nonconformance.ProductionOrderId,
            recording.Hold.Id,
            recording.Hold.Status.ToString(),
            recording.Nonconformance.ManufacturingEventId,
            recording.IsReplay);

    private static bool Required(string value, int maxLength) =>
        value.Length is > 0 && value.Length <= maxLength;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static NonconformanceRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record ParsedReport(
        string SourceSystem,
        string IdempotencyKey,
        string FinishedSerialNumber,
        string DetectedOperationCode,
        string DefectCode,
        string Phenomenon,
        string EvidenceReference,
        DateTimeOffset DetectedAtUtc,
        string Location,
        string CommandHash);
}
