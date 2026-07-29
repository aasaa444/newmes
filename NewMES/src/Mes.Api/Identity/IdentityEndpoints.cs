using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync);
        endpoints.MapGet("/api/identity/me", GetCurrentIdentityAsync)
            .RequireAuthorization();
        endpoints.MapPut("/api/admin/accounts/{userId:guid}/roles", ChangeRolesAsync)
            .RequireAuthorization();
        endpoints.MapPut("/api/admin/accounts/{userId:guid}/active", SetActiveAsync)
            .RequireAuthorization();
        endpoints.MapGet("/api/admin/audit", ReadAuditAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LocalAccountAuthenticator authenticator,
        JwtTokenIssuer tokenIssuer,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var identity = await authenticator.AuthenticateAsync(
            request.Username,
            request.Password,
            context.TraceIdentifier,
            cancellationToken);
        return identity is null
            ? Results.Json(
                new { code = "INVALID_CREDENTIALS_OR_INACTIVE_ACCOUNT" },
                statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(new { accessToken = tokenIssuer.Issue(identity), expiresInSeconds = 28800 });
    }

    private static async Task<IResult> GetCurrentIdentityAsync(
        CurrentIdentityAccessor accessor,
        IdentityAccessService identityAccess,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await identityAccess.DemandCapabilityAsync(
                accessor.Identity,
                BusinessCapability.IdentityContextRead,
                "IDENTITY_CONTEXT_READ",
                "UserAccount",
                accessor.Identity.UserId.ToString(),
                context.TraceIdentifier,
                cancellationToken);
            return Results.Ok(ToResponse(accessor.Identity));
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static async Task<IResult> ChangeRolesAsync(
        Guid userId,
        ChangeRolesRequest request,
        CurrentIdentityAccessor accessor,
        IdentityAccessService identityAccess,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await identityAccess.ChangeRolesAsync(
                accessor.Identity,
                userId,
                request.PrimaryRole,
                request.Roles,
                context.TraceIdentifier,
                cancellationToken);
            return Results.NoContent();
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static async Task<IResult> SetActiveAsync(
        Guid userId,
        SetAccountActiveRequest request,
        CurrentIdentityAccessor accessor,
        IdentityAccessService identityAccess,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await identityAccess.SetAccountActiveAsync(
                accessor.Identity,
                userId,
                request.IsActive,
                context.TraceIdentifier,
                cancellationToken);
            return Results.NoContent();
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static async Task<IResult> ReadAuditAsync(
        int? take,
        CurrentIdentityAccessor accessor,
        IdentityAccessService identityAccess,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var records = await identityAccess.ReadAuditAsync(
                accessor.Identity,
                take ?? 50,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Ok(records.Select(record => new
            {
                record.Id,
                record.OccurredAtUtc,
                record.ActorUserId,
                record.ActorUsername,
                record.ActorRolesSnapshot,
                record.AuthorizedRole,
                record.Capability,
                record.Action,
                record.BusinessObjectType,
                record.BusinessObjectId,
                record.Result,
                record.ReasonCode,
                record.CorrelationId,
            }));
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static object ToResponse(EffectiveIdentity identity) => new
    {
        identity.UserId,
        identity.Username,
        identity.DisplayName,
        identity.PrimaryRole,
        identity.Roles,
        identity.Capabilities,
    };

    private static IResult Forbidden(CapabilityDeniedException exception) =>
        Results.Json(
            new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
            statusCode: StatusCodes.Status403Forbidden);

    public sealed record LoginRequest(string Username, string Password);

    public sealed record ChangeRolesRequest(
        BusinessRole PrimaryRole,
        IReadOnlyCollection<BusinessRole> Roles);

    public sealed record SetAccountActiveRequest(bool IsActive);
}
