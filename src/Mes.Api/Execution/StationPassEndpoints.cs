using System.Security.Claims;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 过站台 API（票 04）：选工位过站、关键件绑定、谱系查询。
/// 写：StationRoles（操作工/班组长/计划员）；谱系只读：AnyBusinessRole。
/// </summary>
public static class StationPassEndpoints
{
    public static void MapStationPassEndpoints(this WebApplication app)
    {
        var station = app.MapGroup("/api").RequireAuthorization("StationRoles");
        var read = app.MapGroup("/api").RequireAuthorization("AnyBusinessRole");

        // 工位列表（过站台下拉）
        read.MapGet("/stations/active", async (MesDbContext db) =>
        {
            var items = await db.WorkStations.AsNoTracking()
                .Include(s => s.BoundProcessStep)
                .Include(s => s.ProductionLine)
                .Where(s => s.IsActive)
                .OrderBy(s => s.Code)
                .Select(s => new ActiveStationDto(
                    s.Id, s.Code, s.Name,
                    s.BoundProcessStepId, s.BoundProcessStep!.Code, s.BoundProcessStep.Name,
                    s.ProductionLine!.Code))
                .ToListAsync();
            return Results.Ok(items);
        });

        station.MapPost("/station/pass", async (
            StationPassRequest req,
            StationPassService svc,
            AuditService audit,
            ClaimsPrincipal user,
            MesDbContext db) =>
        {
            try
            {
                var op = user.Identity?.Name ?? "";
                var serial = await svc.PassAsync(req.WorkStationId, req.SerialNo, req.WorkOrderId, op);
                await audit.WriteAsync("StationPass", op, "ProductSerial", serial.Id.ToString(), serial.SerialNo);

                var cur = serial.CurrentProcessStepId is null
                    ? null
                    : await db.ProcessSteps.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == serial.CurrentProcessStepId);

                return Results.Ok(new SerialStatusResponse(
                    serial.Id,
                    serial.SerialNo,
                    serial.WorkOrderId,
                    serial.Status.ToString(),
                    cur?.Code,
                    cur?.Name));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        station.MapPost("/station/bind-component", async (
            BindComponentRequest req,
            StationPassService svc,
            AuditService audit,
            ClaimsPrincipal user) =>
        {
            try
            {
                var op = user.Identity?.Name ?? "";
                var binding = await svc.BindComponentAsync(
                    req.ProductSerialNo,
                    req.ComponentMaterialId,
                    req.ComponentSerialNo,
                    req.WorkStationId,
                    op);
                await audit.WriteAsync("ComponentBound", op, "ComponentBinding", binding.Id.ToString(),
                    $"{req.ProductSerialNo}+{req.ComponentSerialNo}");
                return Results.Ok(new
                {
                    binding.Id,
                    req.ProductSerialNo,
                    binding.ComponentSerialNo,
                    binding.ComponentMaterialId
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        read.MapGet("/genealogy/{serialNo}", async (string serialNo, StationPassService svc) =>
        {
            try
            {
                var g = await svc.GetGenealogyAsync(serialNo);
                return Results.Ok(g);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }
}

public record StationPassRequest(Guid WorkStationId, string? SerialNo, Guid? WorkOrderId);
public record BindComponentRequest(
    string ProductSerialNo,
    Guid ComponentMaterialId,
    string ComponentSerialNo,
    Guid? WorkStationId);

public record ActiveStationDto(
    Guid Id, string Code, string Name,
    Guid BoundProcessStepId, string StepCode, string StepName,
    string LineCode);

public record SerialStatusResponse(
    Guid Id,
    string SerialNo,
    Guid WorkOrderId,
    string Status,
    string? CurrentStepCode,
    string? CurrentStepName);
