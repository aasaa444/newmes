using System.Text.Json.Serialization;

namespace Mes.Infrastructure.Integration;

public sealed record ProductionOrderIngressRequest(
    string SourceSystem,
    string MessageId,
    string? BusinessKey,
    string? SourceVersion,
    string? ContractVersion,
    string OrderNumber,
    string MaterialCode,
    int PlannedQuantity);

public sealed record ProductionOrderIngressResult(
    string Status,
    string Code,
    string Message,
    Guid? ProductionOrderId,
    [property: JsonIgnore] int HttpStatusCode);

public sealed record ProductionOrderWorkbenchItem(
    Guid Id,
    string OrderNumber,
    string MaterialCode,
    int PlannedQuantity,
    int StartedQuantity,
    int QualifiedQuantity,
    int ScrappedQuantity,
    int UnstartedQuantity,
    int WipQuantity,
    string Status,
    string? SnapshotVersion,
    IReadOnlyList<string> AvailableCommands,
    string? SourceSystem,
    string? SourceReference,
    string? SourceVersion,
    string? InboundStatus,
    string? InboundResultCode);

public sealed record ProductionOrderInboundResultItem(
    Guid InboxMessageId,
    string MessageType,
    string SourceSystem,
    string MessageId,
    string? BusinessKey,
    string? SourceVersion,
    string? ContractVersion,
    Guid? ProductionOrderId,
    string Status,
    string ResultCode,
    string ResultMessage,
    DateTimeOffset ProcessedAtUtc);

public sealed record ProductionOrderWorkbenchResult(
    IReadOnlyList<ProductionOrderWorkbenchItem> Orders,
    IReadOnlyList<ProductionOrderInboundResultItem> InboundResults);
