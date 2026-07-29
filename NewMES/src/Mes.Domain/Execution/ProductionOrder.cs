using Mes.Domain.MasterData;

namespace Mes.Domain.Execution;

public sealed class ProductionOrder
{
    public Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public int PlannedQuantity { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    // Null after an upgrade means the legacy source cannot be proven.
    public string? SourceSystem { get; init; }

    public string? SourceReference { get; init; }

    public byte[] Version { get; private set; } = [];
}
