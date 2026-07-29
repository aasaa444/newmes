using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

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
