using System.IdentityModel.Tokens.Jwt;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Identity;

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
