using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Mes.Infrastructure.IdentityAccess;
using Microsoft.IdentityModel.Tokens;

namespace Mes.Api.Identity;

public sealed class JwtTokenIssuer(IConfiguration configuration, TimeProvider timeProvider)
{
    public string Issue(EffectiveIdentity identity)
    {
        var signingKey = configuration["Security:JwtSigningKey"]
            ?? throw new InvalidOperationException("Security:JwtSigningKey is required.");
        var now = timeProvider.GetUtcNow();
        var token = new JwtSecurityToken(
            issuer: configuration["Authentication:Issuer"] ?? "NewMES",
            audience: configuration["Authentication:Audience"] ?? "NewMES.Web",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, identity.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, identity.Username),
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddHours(8).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
