using System.IdentityModel.Tokens.Jwt;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Identity;

/// <summary>
/// 在 JWT 验证后加载数据库中的当前账号状态和角色，避免长期令牌继续使用已经撤销的权限。
/// </summary>
public sealed class CurrentIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        CurrentIdentityAccessor accessor,
        IdentityAccessService identityAccess)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(subject, out var userId))
        {
            try
            {
                var identity = await identityAccess.GetRequiredIdentityAsync(
                    userId,
                    context.RequestAborted);
                if (!identity.IsActive)
                {
                    await identityAccess.RecordInactiveRequestDeniedAsync(
                        identity,
                        context.TraceIdentifier,
                        context.RequestAborted);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(
                        new { code = "IDENTITY_NOT_ACTIVE" },
                        context.RequestAborted);
                    return;
                }

                accessor.Identity = identity;
            }
            catch (KeyNotFoundException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { code = "IDENTITY_NOT_FOUND" },
                    context.RequestAborted);
                return;
            }
        }

        await next(context);
    }
}
