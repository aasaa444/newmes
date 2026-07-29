namespace Mes.Api.Observability;

public sealed class CorrelationContextAccessor
{
    public string? CorrelationId { get; internal set; }
}
