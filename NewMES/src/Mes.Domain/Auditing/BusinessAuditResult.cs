namespace Mes.Domain.Auditing;

// 业务审计同时记录成功和拒绝，便于说明谁尝试了什么以及系统为什么允许或阻止。
public enum BusinessAuditResult
{
    Succeeded,
    Denied,
}
