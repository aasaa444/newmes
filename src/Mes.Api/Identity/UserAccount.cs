namespace Mes.Api.Identity;

/// <summary>
/// 本地登录账号（第一期不做 AD/企业微信）。
/// PasswordHash 使用 BCrypt，禁止存明文。
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>登录名，唯一，如 planner。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>界面显示名，如「计划员演示」。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>BCrypt 哈希后的密码。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>主角色，取值见 <see cref="AppRoles"/>。</summary>
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
