using Mes.Domain.Materials;

namespace Mes.Infrastructure.Materials;

public sealed record LineSideTransferRequest(
    string? ContractVersion,
    string? SourceSystem,
    string? IdempotencyKey,
    string? SourceDocumentType,
    string? SourceDocumentNumber,
    string? MaterialCode,
    string? LotNumber,
    decimal Quantity,
    string? Unit,
    string? FromParty,
    string? ToParty,
    DateTimeOffset OccurredAtUtc);

public sealed record OrderMaterialRequest(
    string? ContractVersion,
    string? SourceSystem,
    string? IdempotencyKey,
    Guid ProductionOrderId,
    string? MaterialCode,
    string? LotNumber,
    decimal Quantity,
    string? Unit,
    string? SourceDocumentNumber,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record MaterialAdjustmentRequest(
    string? ContractVersion,
    string? SourceSystem,
    string? IdempotencyKey,
    string? MaterialCode,
    string? LotNumber,
    decimal QuantityDelta,
    string? Unit,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record MaterialReversalRequest(
    string? ContractVersion,
    string? SourceSystem,
    string? IdempotencyKey,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record MaterialTransactionResult(
    Guid TransactionId,
    string TransactionType,
    string Status,
    bool IsReplay,
    decimal LineSideBalance,
    decimal? OrderAvailableBalance);

public sealed record MaterialWorkbenchQuery(
    string? MaterialCode,
    string? LotNumber,
    Guid? ProductionOrderId,
    MaterialTransactionType? TransactionType);

public sealed record MaterialLineSideBalance(
    Guid MaterialId,
    string MaterialCode,
    string LotNumber,
    string Unit,
    decimal Quantity);

public sealed record MaterialOrderBalance(
    Guid ProductionOrderId,
    string OrderNumber,
    Guid MaterialId,
    string MaterialCode,
    string LotNumber,
    string Unit,
    decimal AvailableQuantity,
    decimal NetIssuedQuantity);

public sealed record MaterialTransactionView(
    Guid Id,
    string TransactionType,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    string Unit,
    decimal LineSideQuantityDelta,
    decimal OrderAvailableQuantityDelta,
    decimal OrderIssuedQuantityDelta,
    Guid? ProductionOrderId,
    string? OrderNumber,
    string SourceSystem,
    string IdempotencyKey,
    string? SourceDocumentType,
    string? SourceDocumentNumber,
    string? FromParty,
    string? ToParty,
    Guid? ReversesTransactionId,
    string? Reason,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record MaterialWorkbenchResult(
    IReadOnlyList<MaterialLineSideBalance> LineSideBalances,
    IReadOnlyList<MaterialOrderBalance> OrderBalances,
    IReadOnlyList<MaterialTransactionView> Transactions);

public sealed class MaterialTransactionRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
