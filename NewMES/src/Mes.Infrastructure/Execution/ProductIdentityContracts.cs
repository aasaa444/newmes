namespace Mes.Infrastructure.Execution;

public sealed record ControlledIdentifierRequest(
    string Type,
    string Value,
    string SourceType,
    string SourceSystem,
    string SourceReference);

public sealed record IdentitySourceRegistrationRequest(
    string SourceSystem,
    string SourceType,
    string AuthorizedCallerUsername,
    IReadOnlyList<string> AllowedIdentifierTypes,
    string AuthorizationEvidence);

public sealed record IdentitySourceRegistrationResult(
    Guid IdentitySourceId,
    string SourceSystem,
    string SourceType,
    string AuthorizedCallerUsername,
    IReadOnlyList<string> AllowedIdentifierTypes,
    bool IsDemo,
    string Status);

public sealed record ProductIdentityAllocationRequest(
    string SourceSystem,
    string IdempotencyKey,
    string MaterialCode,
    IReadOnlyList<ControlledIdentifierRequest> Identifiers);

public sealed record ProductIdentityAllocationResult(
    Guid ProductIdentityId,
    string SerialNumber,
    string SerialSourceType,
    string SerialSourceSystem,
    string SerialSourceReference,
    bool IsDemo,
    string Status,
    bool IsReplay);

public sealed record StartWipRequest(
    string SourceSystem,
    string IdempotencyKey,
    Guid ProductIdentityId,
    string Location,
    DateTimeOffset OccurredAtUtc);

public sealed record StartWipResult(
    Guid ProductIdentityId,
    string SerialNumber,
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string OrderStatus,
    int StartedQuantity,
    string IdentitySourceSystem,
    string IdentitySourceType,
    bool IsDemo,
    string? NextOperationCode,
    bool IsReplay);

public sealed record ProductIdentityWorkstationResult(
    Guid ProductIdentityId,
    string SerialNumber,
    string Status,
    string IdentitySourceSystem,
    string IdentitySourceType,
    string IdentitySourceReference,
    bool IsDemo,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    string? SnapshotVersion,
    string? NextOperationCode,
    IReadOnlyList<ControlledIdentifierResult> Identifiers);

public sealed record ControlledIdentifierResult(
    string Type,
    string Value,
    string SourceType,
    string SourceSystem,
    string SourceReference,
    bool IsDemo);

public sealed record ProductLabelPrintRequest(
    string TemplateVersion,
    string Printer,
    DateTimeOffset OccurredAtUtc);

public sealed record ProductLabelReasonRequest(
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record ProductLabelReplaceRequest(
    string TemplateVersion,
    string Printer,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record ProductLabelCommandResult(
    Guid LabelId,
    Guid ProductIdentityId,
    string Status,
    string EventType);

public sealed record ProductIdentityCorrectionRequest(
    string Reason,
    string Location,
    DateTimeOffset OccurredAtUtc);

public sealed record ProductIdentityCommandResult(
    Guid ProductIdentityId,
    string SerialNumber,
    string Status,
    Guid? ProductionOrderId);

public sealed class ProductIdentityRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
