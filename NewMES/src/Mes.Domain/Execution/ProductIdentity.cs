using Mes.Domain.MasterData;

namespace Mes.Domain.Execution;

// 一台成品的唯一身份与当前执行位置，连接订单快照、制造事件和完整产品谱系。
public sealed class ProductIdentity
{
    public Guid Id { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public required string SerialNumber { get; init; }

    public IdentitySourceType SerialSourceType { get; init; }

    public required string SerialSourceSystem { get; init; }

    public required string SerialSourceReference { get; init; }

    public bool IsDemo { get; init; }

    public ProductIdentityStatus Status { get; set; }

    public Guid? ProductionOrderId { get; set; }

    public ProductionOrder? ProductionOrder { get; set; }

    public Guid? ExecutionSnapshotId { get; set; }

    public ProductionOrderExecutionSnapshot? ExecutionSnapshot { get; set; }

    public string? NextOperationCode { get; set; }

    public DateTimeOffset AllocatedAtUtc { get; init; }

    public DateTimeOffset? BoundAtUtc { get; set; }

    public string? StartSourceSystem { get; set; }

    public string? StartIdempotencyKey { get; set; }

    public string? StartCommandHash { get; set; }

    public required string AllocationSourceSystem { get; init; }

    public required string AllocationIdempotencyKey { get; init; }

    public required string AllocationCommandHash { get; init; }

    public byte[] Version { get; private set; } = [];
}
