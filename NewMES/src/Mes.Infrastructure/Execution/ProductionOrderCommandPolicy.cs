using Mes.Domain.Execution;

namespace Mes.Infrastructure.Execution;

/// <summary>集中定义生产订单允许的状态迁移，供命令服务和测试共享同一套生命周期规则。</summary>
public static class ProductionOrderCommandPolicy
{
    public static IReadOnlyList<string> AvailableCommands(
        ProductionOrderStatus status,
        bool hasCompleteSourceEvidence,
        int startedQuantity,
        int qualifiedQuantity,
        int scrappedQuantity,
        int openQualityHoldQuantity,
        bool warehouseHandoffCompleted,
        bool erpReconciled) => status switch
        {
            ProductionOrderStatus.Received when hasCompleteSourceEvidence => ["Release", "Cancel"],
            ProductionOrderStatus.Received => ["Cancel"],
            ProductionOrderStatus.Released => ["Cancel"],
            ProductionOrderStatus.InProduction when
                startedQuantity > 0
                && startedQuantity == qualifiedQuantity + scrappedQuantity
                && openQualityHoldQuantity == 0 => ["Pause", "CompleteExecution"],
            ProductionOrderStatus.InProduction => ["Pause"],
            ProductionOrderStatus.Paused => ["Resume"],
            ProductionOrderStatus.ExecutionCompleted when
                warehouseHandoffCompleted && erpReconciled => ["Close"],
            _ => [],
        };
}
