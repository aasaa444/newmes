namespace Mes.Domain.Identity;

// 用户与可组合业务角色的关联；一个用户可以同时承担多个经批准的岗位职责。
public sealed class UserRoleAssignment
{
    public Guid UserAccountId { get; init; }

    public BusinessRole Role { get; init; }

    public UserAccount UserAccount { get; init; } = null!;
}
