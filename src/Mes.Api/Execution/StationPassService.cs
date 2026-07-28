using Mes.Api.Data;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 过站与谱系：首站建 SN、防跳站、关键件绑定、路线前进。
/// 默认：有在制 SN 过站不强制齐套拦截（CONTEXT 齐套默认提示不强制）；
/// 关键件绑定要求工单上该物料仍有 PendingQty。
/// </summary>
public class StationPassService(MesDbContext db)
{
    /// <summary>
    /// 在指定工位对 SN 做合格过站。
    /// serialNo 空且为首站 → 系统发号；非空 → 扫入已有码或续过站。
    /// workOrderId 在「新开个体」时必填。
    /// </summary>
    public async Task<ProductSerial> PassAsync(
        Guid workStationId,
        string? serialNo,
        Guid? workOrderId,
        string operatorUserName,
        CancellationToken ct = default)
    {
        var station = await db.WorkStations
            .Include(s => s.BoundProcessStep)
            .FirstOrDefaultAsync(s => s.Id == workStationId && s.IsActive, ct)
            ?? throw new InvalidOperationException("work station not found");

        var stationStep = station.BoundProcessStep
            ?? await db.ProcessSteps.FirstAsync(p => p.Id == station.BoundProcessStepId, ct);

        ProductSerial serial;
        var isNew = false;

        if (string.IsNullOrWhiteSpace(serialNo))
        {
            // 系统发号：必须指定已下达/生产中工单，且工位必须是该路线首序
            if (workOrderId is null)
            {
                throw new InvalidOperationException("workOrderId required when creating serial by system number");
            }

            var wo = await LoadOpenWorkOrderAsync(workOrderId.Value, ct);
            await EnsureFirstStepAsync(wo, stationStep, ct);
            serialNo = await NextSerialNoAsync(ct);
            serial = await CreateSerialAsync(wo, serialNo, stationStep, ct);
            isNew = true;
        }
        else
        {
            serialNo = serialNo.Trim().ToUpperInvariant();
            serial = await db.ProductSerials
                .Include(s => s.WorkOrder)
                .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct);

            if (serial is null)
            {
                // 扫入新码上线
                if (workOrderId is null)
                {
                    throw new InvalidOperationException("workOrderId required to start a new serial");
                }

                var wo = await LoadOpenWorkOrderAsync(workOrderId.Value, ct);
                await EnsureFirstStepAsync(wo, stationStep, ct);
                await EnsureSerialNotOnOtherOpenOrderAsync(serialNo, ct);
                serial = await CreateSerialAsync(wo, serialNo, stationStep, ct);
                isNew = true;
            }
            else
            {
                if (serial.Status != ProcessStepStatus.InProcess)
                {
                    throw new InvalidOperationException($"serial status is {serial.Status}, cannot pass");
                }

                // 防跳站：工位工序必须等于 SN 当前工序
                if (serial.CurrentProcessStepId != station.BoundProcessStepId)
                {
                    var cur = serial.CurrentProcessStepId is null
                        ? "(none)"
                        : (await db.ProcessSteps.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.Id == serial.CurrentProcessStepId, ct))?.Code ?? "?";
                    throw new InvalidOperationException(
                        $"anti-skip: station step is {stationStep.Code}, serial current step is {cur}");
                }
            }
        }

        // 写入过站履历
        db.SerialPassRecords.Add(new SerialPassRecord
        {
            Id = Guid.NewGuid(),
            ProductSerialId = serial.Id,
            ProcessStepId = stationStep.Id,
            WorkStationId = station.Id,
            Result = "Pass",
            OperatorUserName = operatorUserName,
            OccurredAt = DateTimeOffset.UtcNow
        });

        // 前进到下一工序或标记路线完成
        var routeId = serial.WorkOrder!.ProcessRouteId
            ?? throw new InvalidOperationException("work order has no process route");
        var steps = await db.ProcessSteps.AsNoTracking()
            .Where(p => p.ProcessRouteId == routeId)
            .OrderBy(p => p.Sequence)
            .ToListAsync(ct);

        var idx = steps.FindIndex(p => p.Id == stationStep.Id);
        if (idx < 0)
        {
            throw new InvalidOperationException("station step not on work order route");
        }

        if (idx + 1 < steps.Count)
        {
            serial.CurrentProcessStepId = steps[idx + 1].Id;
            serial.Status = ProcessStepStatus.InProcess;
        }
        else
        {
            serial.CurrentProcessStepId = null;
            serial.Status = ProcessStepStatus.RouteCompleted;
            // 路线走完仍占在制直到入库（票 06）；计数在报废/入库时再减
        }

        if (isNew)
        {
            var wo = await db.WorkOrders.FirstAsync(w => w.Id == serial.WorkOrderId, ct);
            if (wo.Status == WorkOrderStatus.Released)
            {
                wo.Status = WorkOrderStatus.InProcess;
            }

            wo.InProcessSerialCount += 1;
        }

        await db.SaveChangesAsync(ct);
        return serial;
    }

    /// <summary>绑定关键件 SN 到成品；扣减工单该物料 Pending→Consumed。</summary>
    public async Task<ComponentBinding> BindComponentAsync(
        string productSerialNo,
        Guid componentMaterialId,
        string componentSerialNo,
        Guid? workStationId,
        string operatorUserName,
        CancellationToken ct = default)
    {
        productSerialNo = productSerialNo.Trim().ToUpperInvariant();
        componentSerialNo = componentSerialNo.Trim().ToUpperInvariant();

        var serial = await db.ProductSerials
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.SerialNo == productSerialNo, ct)
            ?? throw new InvalidOperationException("product serial not found");

        if (serial.Status is ProcessStepStatus.Scrapped)
        {
            throw new InvalidOperationException("cannot bind to scrapped serial");
        }

        var mat = await db.Materials.FirstOrDefaultAsync(m => m.Id == componentMaterialId, ct)
            ?? throw new InvalidOperationException("component material not found");

        if (!mat.IsKeyComponent)
        {
            throw new InvalidOperationException("material is not a key component");
        }

        // 同一关键件 SN 全局不重复绑定
        if (await db.ComponentBindings.AnyAsync(b => b.ComponentSerialNo == componentSerialNo, ct))
        {
            throw new InvalidOperationException("component serial already bound");
        }

        var issue = await db.WorkOrderIssueLines
            .FirstOrDefaultAsync(i => i.WorkOrderId == serial.WorkOrderId && i.MaterialId == componentMaterialId, ct)
            ?? throw new InvalidOperationException("no issued quantity for this component on the work order; issue material first");

        if (issue.PendingQty < 1)
        {
            throw new InvalidOperationException("no pending (unbound) quantity for this key component on the work order");
        }

        issue.PendingQty -= 1;
        issue.ConsumedQty += 1;

        Guid? stepId = null;
        if (workStationId is not null)
        {
            var st = await db.WorkStations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == workStationId, ct);
            stepId = st?.BoundProcessStepId;
        }

        stepId ??= serial.CurrentProcessStepId;

        var binding = new ComponentBinding
        {
            Id = Guid.NewGuid(),
            ProductSerialId = serial.Id,
            ComponentMaterialId = mat.Id,
            ComponentSerialNo = componentSerialNo,
            ProcessStepId = stepId,
            BoundAt = DateTimeOffset.UtcNow,
            OperatorUserName = operatorUserName
        };
        db.ComponentBindings.Add(binding);
        await db.SaveChangesAsync(ct);
        return binding;
    }

    /// <summary>按成品 SN 查谱系：过站履历 + 关键件绑定。</summary>
    public async Task<GenealogyDto> GetGenealogyAsync(string serialNo, CancellationToken ct = default)
    {
        serialNo = serialNo.Trim().ToUpperInvariant();
        var serial = await db.ProductSerials.AsNoTracking()
            .Include(s => s.WorkOrder)!.ThenInclude(w => w!.FinishedMaterial)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct)
            ?? throw new InvalidOperationException("serial not found");

        var passes = await db.SerialPassRecords.AsNoTracking()
            .Include(p => p.ProcessStep)
            .Include(p => p.WorkStation)
            .Where(p => p.ProductSerialId == serial.Id)
            .ToListAsync(ct);

        var bindings = await db.ComponentBindings.AsNoTracking()
            .Include(b => b.ComponentMaterial)
            .Where(b => b.ProductSerialId == serial.Id)
            .ToListAsync(ct);

        var curCode = serial.CurrentProcessStepId is null
            ? null
            : (await db.ProcessSteps.AsNoTracking().FirstOrDefaultAsync(p => p.Id == serial.CurrentProcessStepId, ct))?.Code;

        return new GenealogyDto(
            serial.SerialNo,
            serial.WorkOrder!.OrderNo,
            serial.WorkOrder.FinishedMaterial?.Code ?? "",
            serial.Status.ToString(),
            curCode,
            passes.OrderBy(p => p.OccurredAt).Select(p => new PassEventDto(
                p.ProcessStep?.Code ?? "",
                p.ProcessStep?.Name ?? "",
                p.WorkStation?.Code,
                p.Result,
                p.OperatorUserName,
                p.OccurredAt)).ToList(),
            bindings.OrderBy(b => b.BoundAt).Select(b => new BindingEventDto(
                b.ComponentMaterial?.Code ?? "",
                b.ComponentSerialNo,
                b.BoundAt,
                b.OperatorUserName)).ToList());
    }

    private async Task<WorkOrder> LoadOpenWorkOrderAsync(Guid id, CancellationToken ct)
    {
        var wo = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new InvalidOperationException("work order not found");
        if (wo.Status is not (WorkOrderStatus.Released or WorkOrderStatus.InProcess))
        {
            throw new InvalidOperationException("work order must be released or in process");
        }

        if (wo.ProcessRouteId is null)
        {
            throw new InvalidOperationException("work order has no frozen route");
        }

        return wo;
    }

    private async Task EnsureFirstStepAsync(WorkOrder wo, ProcessStep stationStep, CancellationToken ct)
    {
        var first = await db.ProcessSteps.AsNoTracking()
            .Where(p => p.ProcessRouteId == wo.ProcessRouteId)
            .OrderBy(p => p.Sequence)
            .FirstAsync(ct);
        if (first.Id != stationStep.Id)
        {
            throw new InvalidOperationException(
                $"new serial must start at first step {first.Code}, station is {stationStep.Code}");
        }
    }

    private async Task EnsureSerialNotOnOtherOpenOrderAsync(string serialNo, CancellationToken ct)
    {
        var existing = await db.ProductSerials
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct);
        if (existing is null) return;

        var st = existing.WorkOrder!.Status;
        if (st is not (WorkOrderStatus.Closed or WorkOrderStatus.Cancelled))
        {
            throw new InvalidOperationException("serial already linked to an open work order");
        }
    }

    private async Task<ProductSerial> CreateSerialAsync(
        WorkOrder wo, string serialNo, ProcessStep firstStep, CancellationToken ct)
    {
        await EnsureSerialNotOnOtherOpenOrderAsync(serialNo, ct);
        var serial = new ProductSerial
        {
            Id = Guid.NewGuid(),
            SerialNo = serialNo,
            WorkOrderId = wo.Id,
            // 过站前当前工序就是本站；Pass 后会推进
            CurrentProcessStepId = firstStep.Id,
            Status = ProcessStepStatus.InProcess,
            CreatedAt = DateTimeOffset.UtcNow,
            WorkOrder = wo
        };
        db.ProductSerials.Add(serial);
        await db.SaveChangesAsync(ct);
        // 重新加载带 WorkOrder
        return await db.ProductSerials.Include(s => s.WorkOrder).FirstAsync(s => s.Id == serial.Id, ct);
    }

    private async Task<string> NextSerialNoAsync(CancellationToken ct)
    {
        // 简单发号：SN-yyyyMMdd-随机；碰撞则重试
        for (var i = 0; i < 8; i++)
        {
            var no = $"SN-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
            if (!await db.ProductSerials.AnyAsync(s => s.SerialNo == no, ct))
            {
                return no;
            }
        }

        return $"SN-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    }
}

public record PassEventDto(
    string StepCode,
    string StepName,
    string? StationCode,
    string Result,
    string? OperatorUserName,
    DateTimeOffset OccurredAt);

public record BindingEventDto(
    string ComponentMaterialCode,
    string ComponentSerialNo,
    DateTimeOffset BoundAt,
    string? OperatorUserName);

public record GenealogyDto(
    string SerialNo,
    string WorkOrderNo,
    string FinishedMaterialCode,
    string Status,
    string? CurrentStepCode,
    List<PassEventDto> Passes,
    List<BindingEventDto> Bindings);
