namespace Mes.Domain.Identity;

// 本地账号当前状态。角色在每次请求时从数据库重新投影，因此停用和调岗无需等待令牌过期。
public sealed class UserAccount
{
    public Guid Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public bool IsActive { get; set; }

    public string? PasswordHash { get; set; }

    public BusinessRole? PrimaryRole { get; set; }

    public ICollection<UserRoleAssignment> RoleAssignments { get; } = [];
}
