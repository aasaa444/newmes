namespace Mes.Api.Observability;

/// <summary>在请求作用域内传递关联号，使业务审计、错误响应和日志能够串成同一条证据链。</summary>
public sealed class CorrelationContextAccessor
{
    public string? CorrelationId { get; internal set; }
}
