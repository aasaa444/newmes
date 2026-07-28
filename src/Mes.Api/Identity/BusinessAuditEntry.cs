namespace Mes.Api.Identity;

/// <summary>
/// 业务审计 — who did what to which subject (separate from product genealogy).
/// </summary>
public class BusinessAuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorUserName { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? Detail { get; set; }
}
