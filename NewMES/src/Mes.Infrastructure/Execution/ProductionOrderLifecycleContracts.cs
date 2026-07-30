namespace Mes.Infrastructure.Execution;

// 订单生命周期契约只暴露受控命令，不允许调用方直接提交目标状态，从接口层避免越级跳转。
public enum ProductionOrderCommand
{
    Release,
    Pause,
    Resume,
    Cancel,
    CompleteExecution,
    Close,
}

public sealed record ProductionOrderCommandResult(
    Guid ProductionOrderId,
    string Status,
    string? SnapshotVersion);

public sealed class ProductionOrderCommandRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
