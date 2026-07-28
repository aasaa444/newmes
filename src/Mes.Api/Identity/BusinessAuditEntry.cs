namespace Mes.Api.Identity;

/// <summary>
/// 业务审计条目：记录「人」的操作（登录、改主数据、日后过站等）。
/// 与「谱系 Genealogy」分工：谱系描述产品 SN 经历，审计描述操作者行为。
/// </summary>
public class BusinessAuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>动作码，如 Login、MaterialCreated。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>操作者登录名。</summary>
    public string ActorUserName { get; set; } = string.Empty;

    /// <summary>对象类型，如 Material、User。</summary>
    public string? SubjectType { get; set; }

    /// <summary>对象 Id 的字符串形式。</summary>
    public string? SubjectId { get; set; }

    /// <summary>补充说明（编码、原因等）。</summary>
    public string? Detail { get; set; }
}
