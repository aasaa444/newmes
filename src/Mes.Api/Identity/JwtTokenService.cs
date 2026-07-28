using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Mes.Api.Identity;

/// <summary>appsettings.json 中 Jwt 节。</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "Mes";
    public string Audience { get; set; } = "Mes";

    /// <summary>HMAC 密钥，开发默认值必须在试点前轮换。</summary>
    public string SigningKey { get; set; } = "DEV_ONLY_CHANGE_ME_TO_A_LONG_RANDOM_SECRET_KEY_32+";

    public int ExpireMinutes { get; set; } = 480;
}

/// <summary>签发访问令牌；Role 与能力 claim 供 Authorize / 前端落地使用。</summary>
public class JwtTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    public const string ClaimCanViewOpsOverview = "can_view_ops_overview";
    public const string ClaimDefaultShell = "default_shell";
    public const string ClaimDefaultPath = "default_path";

    private readonly JwtOptions _opt = options.Value;

    public string CreateToken(UserAccount user)
    {
        var landing = RoleAccess.ForUser(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role),
            new("display_name", user.DisplayName),
            new(ClaimCanViewOpsOverview, landing.CanViewOpsOverview ? "true" : "false"),
            new(ClaimDefaultShell, landing.DefaultShell),
            new(ClaimDefaultPath, landing.DefaultPath)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opt.ExpireMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
