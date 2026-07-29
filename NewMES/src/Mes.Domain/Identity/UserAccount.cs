namespace Mes.Domain.Identity;

public sealed class UserAccount
{
    public Guid Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public bool IsActive { get; set; }
}
