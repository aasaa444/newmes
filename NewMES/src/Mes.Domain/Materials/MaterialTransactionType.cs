namespace Mes.Domain.Materials;

public enum MaterialTransactionType
{
    LineSideTransfer,
    OrderIssue,
    OrderReturn,
    Consumption,
    Reversal,
    Adjustment,
}
