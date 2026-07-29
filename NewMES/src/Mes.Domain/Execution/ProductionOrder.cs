using Mes.Domain.MasterData;

namespace Mes.Domain.Execution;

public sealed class ProductionOrder
{
    public Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public int PlannedQuantity { get; init; }

    public ProductionOrderStatus Status { get; set; }

    public int StartedQuantity { get; set; }

    public int QualifiedQuantity { get; set; }

    public int ScrappedQuantity { get; set; }

    public int OpenQualityHoldQuantity { get; set; }

    public bool WarehouseHandoffCompleted { get; set; }

    public bool ErpReconciled { get; set; }

    public DateTimeOffset? ReleasedAtUtc { get; set; }

    public DateTimeOffset? ExecutionCompletedAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    // Null after an upgrade means the legacy source cannot be proven.
    public string? SourceSystem { get; init; }

    public string? SourceReference { get; init; }

    // Null after an upgrade means the legacy source version cannot be proven.
    public string? SourceVersion { get; init; }

    public byte[] Version { get; private set; } = [];
}
