using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>把持久化角色分配投影成请求期身份，能力只能来自受控角色矩阵。</summary>
internal static class EffectiveIdentityProjector
{
    public static EffectiveIdentity From(UserAccount account)
    {
        var roles = account.RoleAssignments
            .Select(assignment => assignment.Role)
            .Distinct()
            .Order()
            .ToArray();
        return new EffectiveIdentity(
            account.Id,
            account.Username,
            account.DisplayName,
            account.IsActive,
            account.PrimaryRole,
            roles,
            RoleCapabilityMatrix.GetEffectiveCapabilities(roles));
    }
}
