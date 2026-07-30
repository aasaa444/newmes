namespace Mes.Infrastructure.Execution;

// 本文件定义装配投料、解绑、替换、冲正和正反向追溯查询的应用层契约。
public sealed record AssemblyMaterialConsumeRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? OperationCode,
    string? MaterialCode,
    string? ComponentSerialNumber,
    string? LotNumber,
    decimal? Quantity,
    string? Unit,
    string? Location,
    DateTimeOffset OccurredAtUtc);

public sealed record AssemblyMaterialConsumeResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    string MaterialCode,
    string TraceabilityMode,
    Guid? BindingId,
    Guid MaterialTransactionId,
    decimal ConsumedQuantity,
    decimal RemainingQuantity,
    bool OperationCompleted,
    string? NextOperationCode,
    bool IsReplay);

public sealed record AssemblyBindingView(
    Guid BindingId,
    string ComponentSerialNumber,
    string LotNumber,
    decimal Quantity,
    string Unit,
    string Status,
    DateTimeOffset BoundAtUtc);

public sealed record AssemblyMaterialRequirementView(
    string MaterialCode,
    string MaterialName,
    string OperationCode,
    string TraceabilityMode,
    string ConsumptionRule,
    decimal RequiredQuantity,
    decimal ConsumedQuantity,
    decimal RemainingQuantity,
    string Unit,
    IReadOnlyList<AssemblyBindingView> ActiveBindings);

public sealed record AssemblyMaterialRequirementsResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    string? CurrentOperationCode,
    IReadOnlyList<AssemblyMaterialRequirementView> Requirements);

public sealed record ProductMaterialConsumptionView(
    Guid MaterialTransactionId,
    string TransactionType,
    string MaterialCode,
    string TraceabilityMode,
    string OperationCode,
    string? ComponentSerialNumber,
    string LotNumber,
    decimal Quantity,
    string Unit,
    Guid? BindingId,
    string? RelationStatus,
    Guid? ReversesTransactionId,
    string? Reason,
    decimal NetConsumedQuantity,
    DateTimeOffset OccurredAtUtc);

public sealed record ProductMaterialGenealogyResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    IReadOnlyList<ProductMaterialConsumptionView> Consumptions);

public sealed record LotAffectedProductView(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    decimal NetConsumedQuantity,
    string Unit);

public sealed record LotImpactResult(
    string MaterialCode,
    string LotNumber,
    IReadOnlyList<LotAffectedProductView> AffectedProducts);

public sealed record ComponentRelationshipImpactView(
    Guid BindingId,
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string MaterialCode,
    string LotNumber,
    string Status,
    DateTimeOffset BoundAtUtc,
    DateTimeOffset? UnboundAtUtc,
    string? CorrectionReason);

public sealed record ComponentImpactResult(
    string ComponentSerialNumber,
    IReadOnlyList<ComponentRelationshipImpactView> Relationships);

public sealed record AssemblyBindingUnbindRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? Reason,
    string? Location,
    DateTimeOffset OccurredAtUtc);

public sealed record AssemblyBindingReplaceRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? NewComponentSerialNumber,
    string? LotNumber,
    string? Reason,
    string? Location,
    DateTimeOffset OccurredAtUtc);

public sealed record AssemblyBindingCorrectionResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid OriginalBindingId,
    Guid OriginalTransactionId,
    Guid ReversalTransactionId,
    Guid? ReplacementBindingId,
    Guid? ReplacementTransactionId,
    string Status,
    string? NextOperationCode,
    bool IsReplay);

public sealed record AssemblyConsumptionReverseRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? Reason,
    string? Location,
    DateTimeOffset OccurredAtUtc);

public sealed record AssemblyConsumptionReverseResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid OriginalTransactionId,
    Guid ReversalTransactionId,
    string Status,
    string? NextOperationCode,
    bool IsReplay);

public sealed class AssemblyMaterialRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
