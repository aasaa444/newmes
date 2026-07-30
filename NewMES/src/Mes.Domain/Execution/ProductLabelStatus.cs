namespace Mes.Domain.Execution;

// 标签状态保留作废和换标历史，不允许把旧标签记录直接删除。
public enum ProductLabelStatus
{
    Active,
    Voided,
    Replaced,
}
