using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

public static class ExecutionTemplateEndpoints
{
    public static IEndpointRouteBuilder MapExecutionTemplateEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/process/execution-templates",
                PublishAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> PublishAsync(
        ExecutionTemplatePublishRequest request,
        CurrentIdentityAccessor accessor,
        ExecutionTemplateService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await service.PublishAsync(
                accessor.Identity,
                request,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Created(
                $"/api/process/execution-templates/{result.TemplateId}",
                result);
        }
        catch (CapabilityDeniedException exception)
        {
            return Results.Json(
                new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ExecutionTemplateRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message },
                statusCode: exception.HttpStatusCode);
        }
    }
}
