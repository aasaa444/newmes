namespace Mes.Infrastructure.Execution;

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
