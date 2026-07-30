namespace Mes.Domain.Execution;

// 生产订单受控生命周期；状态转换由命令策略验证，不能由任意 CRUD 更新。
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
