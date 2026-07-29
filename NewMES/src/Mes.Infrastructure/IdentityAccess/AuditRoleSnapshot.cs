using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

internal static class AuditRoleSnapshot
{
    public static string From(IEnumerable<BusinessRole> roles) =>
        string.Join(',', roles.Distinct().Order());
}
