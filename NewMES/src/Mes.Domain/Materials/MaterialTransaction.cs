using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;

namespace Mes.Domain.Materials;

// 一笔不可变的物料责任账流水。余额由流水汇总得到，而不是维护一个可被覆盖的库存数字。
public sealed class MaterialTransaction
{
    public Guid Id { get; init; }

    public MaterialTransactionType TransactionType { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public Guid? ProductionOrderId { get; init; }

    public ProductionOrder? ProductionOrder { get; init; }

    public Guid? ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public string? OperationCode { get; init; }

    public TraceabilityMode? TraceabilityMode { get; init; }

    public required string LotNumber { get; init; }

    public decimal Quantity { get; init; }

    public required string Unit { get; init; }

    public decimal LineSideQuantityDelta { get; init; }

    public decimal OrderAvailableQuantityDelta { get; init; }

    public decimal OrderIssuedQuantityDelta { get; init; }

    public required string SourceSystem { get; init; }

    public required string IdempotencyKey { get; init; }

    public string? SourceDocumentType { get; init; }

    public string? SourceDocumentNumber { get; init; }

    public string? FromParty { get; init; }

    public string? ToParty { get; init; }

    public Guid? ReversesTransactionId { get; init; }

    public MaterialTransaction? ReversesTransaction { get; init; }

    public string? Reason { get; init; }

    public Guid ActorUserId { get; init; }

    public UserAccount? ActorUser { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; }

    public required string CommandHash { get; init; }

    public required string CommandHashAlgorithm { get; init; }

    public required string CorrelationId { get; init; }

    public decimal LineSideBalanceAfter { get; init; }

    public decimal? OrderAvailableBalanceAfter { get; init; }
}
