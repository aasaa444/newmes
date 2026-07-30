using Mes.Domain.Execution;
using Mes.Domain.Identity;

namespace Mes.Domain.Quality;

// 一台产品当前的独立质量隔离事实；多个不合格可以关联到同一个有效保留，避免重复隔离。
public sealed class QualityHold
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public Guid ProductionOrderId { get; init; }

    public ProductionOrder? ProductionOrder { get; init; }

    public QualityHoldStatus Status { get; init; }

    public required string StartReasonCode { get; init; }

    public required string StartReason { get; init; }

    public required string ReleaseCondition { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public Guid StartedByUserId { get; init; }

    public UserAccount? StartedByUser { get; init; }

    public required string StartedByUsername { get; init; }

    public Guid ManufacturingEventId { get; init; }

    public ManufacturingEvent? ManufacturingEvent { get; init; }

    public required string CorrelationId { get; init; }

    public ICollection<NonconformanceRecord> Nonconformances { get; } = [];
}
