using System.Security.Claims;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>经营总览端点（票 03）。需 OpsOverviewRead 授权。</summary>
public static class OpsOverviewEndpoints
{
    public static void MapOpsOverviewEndpoints(this WebApplication app)
    {
        // 总览五块
        app.MapGet("/api/ops/overview", async (
            [FromServices] OpsOverviewService svc,
            ClaimsPrincipal user) =>
        {
            var data = await svc.GetOverviewAsync();
            return Results.Ok(data);
        })
        .RequireAuthorization("OpsOverviewRead");

        // 在制 SN 列表（可按工序过滤）
        app.MapGet("/api/ops/wip", async (
            [FromQuery] Guid? processStepId,
            [FromServices] OpsOverviewService svc) =>
        {
            var rows = await svc.GetWipSerialsAsync(processStepId);
            return Results.Ok(rows);
        })
        .RequireAuthorization("AnyBusinessRole");

        // 工单摘要（inProcess / releasedNotStarted）
        app.MapGet("/api/ops/orders", async (
            [FromQuery] string? bucket,
            [FromServices] OpsOverviewService svc) =>
        {
            var b = string.IsNullOrWhiteSpace(bucket) ? "inProcess" : bucket;
            var rows = await svc.GetOrderSummaryAsync(b);
            return Results.Ok(rows);
        })
        .RequireAuthorization("AnyBusinessRole");
    }
}
