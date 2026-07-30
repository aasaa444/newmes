namespace Mes.Domain.Execution;

// 身份值的权威来源类型，用于区分生产身份与明确隔离的演示身份。
public enum IdentitySourceType
{
    Erp,
    LabelSystem,
    MesControlledPool,
    DemoControlledPool,
}
