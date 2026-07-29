using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

public sealed record EffectiveIdentity(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsActive,
    BusinessRole? PrimaryRole,
    IReadOnlyList<BusinessRole> Roles,
    IReadOnlyCollection<BusinessCapability> Capabilities);
