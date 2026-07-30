using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>一次请求内使用的有效身份快照，包含账号状态、角色和由角色投影出的能力。</summary>
public sealed record EffectiveIdentity(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsActive,
    BusinessRole? PrimaryRole,
    IReadOnlyList<BusinessRole> Roles,
    IReadOnlyCollection<BusinessCapability> Capabilities);
