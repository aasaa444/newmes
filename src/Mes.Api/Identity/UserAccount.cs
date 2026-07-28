namespace Mes.Api.Identity;

public class UserAccount
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Single primary role (CONTEXT: 默认单主角色).</summary>
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
