using System.Text.Json.Serialization;

namespace Mes.Infrastructure.Integration;

public sealed record ProductionOrderIngressRequest(
    string SourceSystem,
    string MessageId,
    string BusinessKey,
    string SourceVersion,
    string ContractVersion,
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
    string Status,
    string? SourceSystem,
    string? SourceReference,
    string? SourceVersion,
    string? InboundStatus,
    string? InboundResultCode);
