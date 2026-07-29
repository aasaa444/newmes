namespace Mes.Domain.Execution;

public enum ProductionOrderStatus
{
    Received,
    Released,
    InProduction,
    Paused,
    ExecutionCompleted,
    Closed,
    Cancelled,
}
