namespace Mes.Domain.Materials;

// 物料责任账中的业务动作。每种动作都追加新流水，不能通过修改旧流水改变库存事实。
public enum MaterialTransactionType
{
    LineSideTransfer,
    OrderIssue,
    OrderReturn,
    Consumption,
    Reversal,
    Adjustment,
}
