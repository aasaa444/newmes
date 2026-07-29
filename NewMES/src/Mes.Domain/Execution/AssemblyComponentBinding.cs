using Mes.Domain.Identity;
using Mes.Domain.Materials;
using Mes.Domain.MasterData;

namespace Mes.Domain.Execution;

public sealed class AssemblyComponentBinding
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ExecutionSnapshotId { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public required string ComponentSerialNumber { get; init; }

    public required string LotNumber { get; init; }

    public decimal Quantity { get; init; }

    public required string Unit { get; init; }

    public required string OperationCode { get; init; }

    public Guid ConsumptionTransactionId { get; init; }

    public MaterialTransaction? ConsumptionTransaction { get; init; }

    public Guid BindingEventId { get; init; }

    public ManufacturingEvent? BindingEvent { get; init; }

    public bool IsActive { get; set; }

    public DateTimeOffset BoundAtUtc { get; init; }

    public Guid BoundByUserId { get; init; }

    public UserAccount? BoundByUser { get; init; }

    public DateTimeOffset? UnboundAtUtc { get; set; }

    public Guid? UnboundByUserId { get; set; }

    public string? CorrectionReason { get; set; }

    public byte[] Version { get; private set; } = [];
}
