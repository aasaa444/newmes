using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Quality;

/// <summary>为质量工作台组合不合格、产品、订单、关联测试和当前保留状态，不负责处置或解除保留。</summary>
public sealed class NonconformanceQueryService(
    MesDbContext context,
    IdentityAccessService identityAccess)
{
    public async Task<NonconformanceWorkbenchResult> ReadAsync(
        EffectiveIdentity actor,
        string? finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.QualityQueueRead,
            "NONCONFORMANCE_QUEUE_READ",
            "NonconformanceRecord",
            string.IsNullOrWhiteSpace(finishedSerialNumber) ? "queue" : finishedSerialNumber.Trim(),
            correlationId,
            cancellationToken);

        var query = context.NonconformanceRecords
            .AsNoTracking()
            .Include(item => item.ProductIdentity)
            .Include(item => item.ProductionOrder)
            .Include(item => item.QualityHold)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(finishedSerialNumber))
        {
            var serialNumber = finishedSerialNumber.Trim();
            query = query.Where(item => item.ProductIdentity!.SerialNumber == serialNumber);
        }

        var records = await query
            .OrderByDescending(item => item.RecordedAtUtc)
            .ToArrayAsync(cancellationToken);
        return new NonconformanceWorkbenchResult(records.Select(item =>
            new NonconformanceWorkbenchItem(
                item.Id,
                item.ReferenceNumber,
                item.Status.ToString(),
                item.ProductIdentityId,
                item.ProductIdentity!.SerialNumber,
                item.ProductionOrderId,
                item.ProductionOrder!.OrderNumber,
                item.DetectedOperationCode,
                item.DefectCode,
                item.Phenomenon,
                item.EvidenceReference,
                item.RelatedTestRunId,
                item.QualityHoldId,
                item.QualityHold!.Status.ToString(),
                item.QualityHold.StartReasonCode,
                item.QualityHold.StartReason,
                item.QualityHold.ReleaseCondition,
                item.QualityHold.StartedAtUtc,
                item.DetectedAtUtc,
                item.ReportedByUsername,
                item.Location,
                item.CorrelationId)).ToArray());
    }
}
