using System.Security.Claims;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.MasterData;

/// <summary>
/// 执行主数据 HTTP 端点（票 02）。
/// 读：AnyBusinessRole（计划员/操作工/班组长均可查，过站前要认物料/工位）。
/// 写：PlannerOnly（非计划员 POST → 403）。
/// 路径挂在 /api/*，由 Program 调用 MapMasterDataEndpoints()。
/// </summary>
public static class MasterDataEndpoints
{
    public static RouteGroupBuilder MapMasterDataEndpoints(this WebApplication app)
    {
        // 两组路由共享路径前缀，授权策略不同
        var read = app.MapGroup("/api").RequireAuthorization("AnyBusinessRole");
        var write = app.MapGroup("/api").RequireAuthorization("PlannerOnly");

        // ----- 物料 -----
        read.MapGet("/materials", async (MesDbContext db) =>
        {
            var items = await db.Materials.AsNoTracking()
                .OrderBy(m => m.Code)
                .Select(m => new MaterialResponse(m.Id, m.Code, m.Name, m.IsFinishedGood, m.IsKeyComponent, m.RequiresSerialNumber, m.IsActive))
                .ToListAsync();
            return Results.Ok(items);
        });

        write.MapPost("/materials", async (CreateMaterialRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "code and name are required" });
            }

            var code = req.Code.Trim().ToUpperInvariant();
            if (await db.Materials.AnyAsync(m => m.Code == code))
            {
                return Results.Conflict(new { error = "material code already exists" });
            }

            var entity = new Material
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = req.Name.Trim(),
                IsFinishedGood = req.IsFinishedGood,
                IsKeyComponent = req.IsKeyComponent,
                RequiresSerialNumber = req.RequiresSerialNumber,
                IsActive = true
            };
            db.Materials.Add(entity);
            await db.SaveChangesAsync();
            await audit.WriteAsync("MaterialCreated", user.Identity?.Name ?? "", "Material", entity.Id.ToString(), entity.Code);
            return Results.Created($"/api/materials/{entity.Id}",
                new MaterialResponse(entity.Id, entity.Code, entity.Name, entity.IsFinishedGood, entity.IsKeyComponent, entity.RequiresSerialNumber, entity.IsActive));
        });

        write.MapPut("/materials/{id:guid}", async (Guid id, UpdateMaterialRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var entity = await db.Materials.FirstOrDefaultAsync(m => m.Id == id);
            if (entity is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(req.Name))
            {
                entity.Name = req.Name.Trim();
            }

            entity.IsKeyComponent = req.IsKeyComponent;
            entity.RequiresSerialNumber = req.RequiresSerialNumber;
            entity.IsFinishedGood = req.IsFinishedGood;
            entity.IsActive = req.IsActive;
            await db.SaveChangesAsync();
            await audit.WriteAsync("MaterialUpdated", user.Identity?.Name ?? "", "Material", entity.Id.ToString(), entity.Code);
            return Results.Ok(new MaterialResponse(entity.Id, entity.Code, entity.Name, entity.IsFinishedGood, entity.IsKeyComponent, entity.RequiresSerialNumber, entity.IsActive));
        });

        write.MapDelete("/materials/{id:guid}", async (Guid id, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var entity = await db.Materials.FirstOrDefaultAsync(m => m.Id == id);
            if (entity is null)
            {
                return Results.NotFound();
            }

            entity.IsActive = false;
            await db.SaveChangesAsync();
            await audit.WriteAsync("MaterialDeactivated", user.Identity?.Name ?? "", "Material", entity.Id.ToString(), entity.Code);
            return Results.NoContent();
        });

        read.MapGet("/boms", async (MesDbContext db) =>
        {
            var boms = await db.Boms.AsNoTracking()
                .Include(b => b.FinishedMaterial)
                .Include(b => b.Lines).ThenInclude(l => l.ComponentMaterial)
                .OrderBy(b => b.FinishedMaterial!.Code)
                .ToListAsync();
            var result = boms.Select(b => new BomResponse(
                b.Id,
                b.FinishedMaterial!.Code,
                b.Version,
                b.Lines.Select(l => new BomLineResponse(l.ComponentMaterial!.Code, l.QuantityPer)).ToList()
            )).ToList();
            return Results.Ok(result);
        });

        write.MapPost("/boms", async (CreateBomRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var fg = await db.Materials.FirstOrDefaultAsync(m => m.Id == req.FinishedMaterialId && m.IsFinishedGood);
            if (fg is null)
            {
                return Results.BadRequest(new { error = "finishedMaterialId must reference an active finished good" });
            }

            if (req.Lines is null || req.Lines.Count == 0)
            {
                return Results.BadRequest(new { error = "BOM requires at least one line" });
            }

            var componentIds = req.Lines.Select(l => l.ComponentMaterialId).ToList();
            var components = await db.Materials.Where(m => componentIds.Contains(m.Id)).ToListAsync();
            if (components.Count != componentIds.Distinct().Count())
            {
                return Results.BadRequest(new { error = "one or more component materials not found" });
            }

            var bom = new Bom
            {
                Id = Guid.NewGuid(),
                FinishedMaterialId = fg.Id,
                Version = string.IsNullOrWhiteSpace(req.Version) ? "A" : req.Version.Trim(),
                IsActive = true,
                Lines = req.Lines.Select(l => new BomLine
                {
                    Id = Guid.NewGuid(),
                    ComponentMaterialId = l.ComponentMaterialId,
                    QuantityPer = l.QuantityPer
                }).ToList()
            };
            foreach (var line in bom.Lines)
            {
                line.BomId = bom.Id;
            }

            db.Boms.Add(bom);
            await db.SaveChangesAsync();
            await audit.WriteAsync("BomCreated", user.Identity?.Name ?? "", "Bom", bom.Id.ToString(), fg.Code);

            var loaded = await db.Boms.AsNoTracking()
                .Include(b => b.FinishedMaterial)
                .Include(b => b.Lines).ThenInclude(l => l.ComponentMaterial)
                .FirstAsync(b => b.Id == bom.Id);
            return Results.Created($"/api/boms/{bom.Id}", new BomResponse(
                loaded.Id,
                loaded.FinishedMaterial!.Code,
                loaded.Version,
                loaded.Lines.Select(l => new BomLineResponse(l.ComponentMaterial!.Code, l.QuantityPer)).ToList()));
        });

        read.MapGet("/process-routes", async (MesDbContext db) =>
        {
            var routes = await db.ProcessRoutes.AsNoTracking()
                .Include(r => r.Steps)
                .OrderBy(r => r.Code)
                .ToListAsync();
            var result = routes.Select(r => new RouteResponse(
                r.Id,
                r.Code,
                r.Name,
                r.Steps.OrderBy(s => s.Sequence)
                    .Select(s => new StepResponse(s.Id, s.Sequence, s.Code, s.Name))
                    .ToList()
            )).ToList();
            return Results.Ok(result);
        });

        write.MapPost("/process-routes", async (CreateRouteRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var fg = await db.Materials.FirstOrDefaultAsync(m => m.Id == req.FinishedMaterialId && m.IsFinishedGood);
            if (fg is null)
            {
                return Results.BadRequest(new { error = "finishedMaterialId must be a finished good" });
            }

            if (req.Steps is null || req.Steps.Count == 0)
            {
                return Results.BadRequest(new { error = "route requires steps" });
            }

            var code = req.Code.Trim().ToUpperInvariant();
            if (await db.ProcessRoutes.AnyAsync(r => r.Code == code))
            {
                return Results.Conflict(new { error = "route code exists" });
            }

            var route = new ProcessRoute
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = req.Name.Trim(),
                FinishedMaterialId = fg.Id,
                Version = string.IsNullOrWhiteSpace(req.Version) ? "1" : req.Version.Trim(),
                IsActive = true,
                Steps = req.Steps.OrderBy(s => s.Sequence).Select(s => new ProcessStep
                {
                    Id = Guid.NewGuid(),
                    Sequence = s.Sequence,
                    Code = s.Code.Trim().ToUpperInvariant(),
                    Name = s.Name.Trim(),
                    IsQualityStep = s.IsQualityStep,
                    ReworkToSequence = s.ReworkToSequence
                }).ToList()
            };
            foreach (var step in route.Steps)
            {
                step.ProcessRouteId = route.Id;
            }

            db.ProcessRoutes.Add(route);
            await db.SaveChangesAsync();
            await audit.WriteAsync("ProcessRouteCreated", user.Identity?.Name ?? "", "ProcessRoute", route.Id.ToString(), route.Code);

            return Results.Created($"/api/process-routes/{route.Id}", new RouteResponse(
                route.Id,
                route.Code,
                route.Name,
                route.Steps.OrderBy(s => s.Sequence).Select(s => new StepResponse(s.Id, s.Sequence, s.Code, s.Name)).ToList()));
        });

        read.MapGet("/production-lines", async (MesDbContext db) =>
        {
            var items = await db.ProductionLines.AsNoTracking()
                .OrderBy(l => l.Code)
                .Select(l => new LineResponse(l.Id, l.Code, l.Name, l.IsActive))
                .ToListAsync();
            return Results.Ok(items);
        });

        write.MapPost("/production-lines", async (CreateLineRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var code = req.Code.Trim().ToUpperInvariant();
            if (await db.ProductionLines.AnyAsync(l => l.Code == code))
            {
                return Results.Conflict(new { error = "line code exists" });
            }

            var line = new ProductionLine
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = req.Name.Trim(),
                IsActive = true
            };
            db.ProductionLines.Add(line);
            await db.SaveChangesAsync();
            await audit.WriteAsync("ProductionLineCreated", user.Identity?.Name ?? "", "ProductionLine", line.Id.ToString(), line.Code);
            return Results.Created($"/api/production-lines/{line.Id}", new LineResponse(line.Id, line.Code, line.Name, line.IsActive));
        });

        read.MapGet("/work-stations", async (MesDbContext db) =>
        {
            var items = await db.WorkStations.AsNoTracking()
                .Include(s => s.BoundProcessStep)
                .OrderBy(s => s.Code)
                .Select(s => new StationResponse(
                    s.Id,
                    s.Code,
                    s.Name,
                    s.BoundProcessStepId,
                    s.BoundProcessStep!.Code,
                    s.ProductionLineId,
                    s.IsActive))
                .ToListAsync();
            return Results.Ok(items);
        });

        write.MapPost("/work-stations", async (CreateStationRequest req, MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            var line = await db.ProductionLines.FirstOrDefaultAsync(l => l.Id == req.ProductionLineId);
            if (line is null)
            {
                return Results.BadRequest(new { error = "productionLineId not found" });
            }

            var step = await db.ProcessSteps.FirstOrDefaultAsync(s => s.Id == req.BoundProcessStepId);
            if (step is null)
            {
                return Results.BadRequest(new { error = "boundProcessStepId not found — station must bind exactly one process step" });
            }

            var code = req.Code.Trim().ToUpperInvariant();
            if (await db.WorkStations.AnyAsync(s => s.Code == code))
            {
                return Results.Conflict(new { error = "station code exists" });
            }

            var station = new WorkStation
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = req.Name.Trim(),
                ProductionLineId = line.Id,
                BoundProcessStepId = step.Id,
                IsActive = true
            };
            db.WorkStations.Add(station);
            await db.SaveChangesAsync();
            await audit.WriteAsync("WorkStationCreated", user.Identity?.Name ?? "", "WorkStation", station.Id.ToString(), station.Code);

            return Results.Created($"/api/work-stations/{station.Id}",
                new StationResponse(station.Id, station.Code, station.Name, station.BoundProcessStepId, step.Code, station.ProductionLineId, station.IsActive));
        });

        // 幂等补种（库空或被清空后可用）；已有 FG-ROUTER 则 no-op
        write.MapPost("/master-data/seed", async (MesDbContext db, AuditService audit, ClaimsPrincipal user) =>
        {
            MasterDataSeed.EnsureSeeded(db);
            await audit.WriteAsync("MasterDataSeeded", user.Identity?.Name ?? "", "MasterData", null, "EnsureSeeded");
            return Results.Ok(new { seeded = true });
        });

        return read;
    }
}

// ----- 请求/响应 DTO（API 契约；与实体字段对齐但可裁剪）-----

public record CreateMaterialRequest(string Code, string Name, bool IsFinishedGood, bool IsKeyComponent, bool RequiresSerialNumber);
public record UpdateMaterialRequest(string? Name, bool IsFinishedGood, bool IsKeyComponent, bool RequiresSerialNumber, bool IsActive);
public record MaterialResponse(Guid Id, string Code, string Name, bool IsFinishedGood, bool IsKeyComponent, bool RequiresSerialNumber, bool IsActive);

public record CreateBomLineRequest(Guid ComponentMaterialId, decimal QuantityPer);
public record CreateBomRequest(Guid FinishedMaterialId, string? Version, List<CreateBomLineRequest> Lines);
public record BomLineResponse(string ComponentCode, decimal QuantityPer);
public record BomResponse(Guid Id, string FinishedMaterialCode, string Version, List<BomLineResponse> Lines);

public record CreateStepRequest(int Sequence, string Code, string Name, bool IsQualityStep, int? ReworkToSequence);
public record CreateRouteRequest(string Code, string Name, Guid FinishedMaterialId, string? Version, List<CreateStepRequest> Steps);
public record StepResponse(Guid Id, int Sequence, string Code, string Name);
public record RouteResponse(Guid Id, string Code, string Name, List<StepResponse> Steps);

public record CreateLineRequest(string Code, string Name);
public record LineResponse(Guid Id, string Code, string Name, bool IsActive);

public record CreateStationRequest(string Code, string Name, Guid ProductionLineId, Guid BoundProcessStepId);
public record StationResponse(Guid Id, string Code, string Name, Guid BoundProcessStepId, string BoundProcessStepCode, Guid ProductionLineId, bool IsActive);
