namespace Mes.Domain.Execution;

// 成品身份从预分配到投入生产的生命周期；Voided 保留原身份但禁止继续使用。
public enum ProductIdentityStatus
{
    Allocated,
    Bound,
    Voided,
}
