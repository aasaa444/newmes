namespace Mes.Domain.Identity;

public sealed class UserRoleAssignment
{
    public Guid UserAccountId { get; init; }

    public BusinessRole Role { get; init; }

    public UserAccount UserAccount { get; init; } = null!;
}
