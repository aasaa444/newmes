using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>生成稳定排序的角色快照，确保审计证据不依赖用户日后角色变化。</summary>
internal static class AuditRoleSnapshot
{
    public static string From(IEnumerable<BusinessRole> roles) =>
        string.Join(',', roles.Distinct().Order());
}
