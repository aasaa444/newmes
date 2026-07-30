using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

public static class TestSpecificationEndpoints
{
    public static IEndpointRouteBuilder MapTestSpecificationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/quality/test-specifications/versions",
                CreateDraftAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/quality/test-specifications/{code}/versions/{version}/approve",
                ApproveAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/quality/test-specifications/{code}/versions/{version}",
                ReadAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> CreateDraftAsync(
        TestSpecificationDraftRequest request,
        CurrentIdentityAccessor accessor,
        TestSpecificationService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            context.TraceIdentifier,
            async () =>
            {
                var result = await service.CreateDraftAsync(
                    accessor.Identity!,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return Results.Created(
                    $"/api/quality/test-specifications/{result.Code}/versions/{result.Version}",
                    result);
            });

    private static Task<IResult> ApproveAsync(
        string code,
        string version,
        TestSpecificationApprovalRequest request,
        CurrentIdentityAccessor accessor,
        TestSpecificationService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            context.TraceIdentifier,
            async () => Results.Ok(await service.ApproveAsync(
                accessor.Identity!,
                code,
                version,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReadAsync(
        string code,
        string version,
        CurrentIdentityAccessor accessor,
        TestSpecificationService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            context.TraceIdentifier,
            async () => Results.Ok(await service.ReadAsync(
                accessor.Identity!,
                code,
                version,
                context.TraceIdentifier,
                cancellationToken)));

    private static async Task<IResult> ExecuteAsync(
        CurrentIdentityAccessor accessor,
        string correlationId,
        Func<Task<IResult>> execute)
    {
        if (accessor.Identity is null)
        {
            return Results.Json(
                new { code = "AUTHENTICATION_REQUIRED", message = "请先登录后再操作测试规范。", correlationId },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            return await execute();
        }
        catch (CapabilityDeniedException exception)
        {
            return Results.Json(
                new
                {
                    code = exception.ReasonCode,
                    capability = exception.Capability.ToString(),
                    message = "当前账号没有执行该测试规范操作的权限，请核对工艺与质量角色。",
                    correlationId,
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (TestSpecificationRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message, correlationId },
                statusCode: exception.HttpStatusCode);
        }
    }
}
