using Mes.Api.Data;

namespace Mes.Api.Identity;

public class AuditService(MesDbContext db)
{
    public async Task WriteAsync(
        string action,
        string actorUserName,
        string? subjectType = null,
        string? subjectId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        db.AuditEntries.Add(new BusinessAuditEntry
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Action = action,
            ActorUserName = actorUserName,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Detail = detail
        });
        await db.SaveChangesAsync(ct);
    }
}
