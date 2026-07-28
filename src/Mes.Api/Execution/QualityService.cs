using Mes.Api.Data;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 质量判定（票 05）：不合格可 返工 / 隔离 / 报废；隔离后可放行回路线。
/// 失败与再过站履历只追加不删（谱系可查）。
/// 隔离品不得走正常合格过站/完工入库（入库在票 06 再拦）。
/// </summary>
public class QualityService(MesDbContext db)
{
    /// <summary>
    /// 在当前工位对 SN 判定不合格。
    /// disposition: Rework | Isolate | Scrap
    /// reworkToSequence 可选；空则用工序上的 ReworkToSequence，再空则回当前序。
    /// </summary>
    public async Task<ProductSerial> FailAsync(
        Guid workStationId,
        string serialNo,
        string disposition,
        string? reason,
        int? reworkToSequence,
        string operatorUserName,
        CancellationToken ct = default)
    {
        disposition = disposition.Trim();
        if (disposition is not ("Rework" or "Isolate" or "Scrap"))
        {
            throw new InvalidOperationException("不合格处置须为：返工、隔离或报废");
        }

        serialNo = serialNo.Trim().ToUpperInvariant();
        var station = await db.WorkStations
            .Include(s => s.BoundProcessStep)
            .FirstOrDefaultAsync(s => s.Id == workStationId && s.IsActive, ct)
            ?? throw new InvalidOperationException("未找到该工位");

        var stationStep = station.BoundProcessStep
            ?? await db.ProcessSteps.FirstAsync(p => p.Id == station.BoundProcessStepId, ct);

        var serial = await db.ProductSerials
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct)
            ?? throw new InvalidOperationException("未找到该成品序列号");

        if (serial.Status is ProcessStepStatus.Scrapped)
        {
            throw new InvalidOperationException("该序列号已报废");
        }

        if (serial.Status is ProcessStepStatus.Isolated)
        {
            throw new InvalidOperationException("序列号处于隔离中：请先放行后再处理，或直接报废");
        }

        // 路线已完成：仅允许隔离/报废（例如终检后发现异常），不要求防跳站
        var routeDone = serial.Status == ProcessStepStatus.RouteCompleted;
        if (routeDone && disposition is "Rework")
        {
            throw new InvalidOperationException("路线已完成的序列号不能返工，请隔离或报废");
        }

        if (!routeDone)
        {
            // 防跳站：在制不合格须在当前应站登记
            if (serial.CurrentProcessStepId != station.BoundProcessStepId)
            {
                var cur = serial.CurrentProcessStepId is null
                    ? "(none)"
                    : (await db.ProcessSteps.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == serial.CurrentProcessStepId, ct))?.Code ?? "?";
                throw new InvalidOperationException(
                    $"防跳站：工位工序为 {stationStep.Code}，序列号当前工序为 {cur}");
            }
        }
        else if (serial.Status is not (ProcessStepStatus.InProcess or ProcessStepStatus.RouteCompleted))
        {
            throw new InvalidOperationException($"序列号当前状态为 {serial.Status}，不能做不合格处理");
        }

        var remark = string.IsNullOrWhiteSpace(reason) ? disposition : $"{disposition}: {reason.Trim()}";

        // 履历：Fail（历史保留）
        db.SerialPassRecords.Add(new SerialPassRecord
        {
            Id = Guid.NewGuid(),
            ProductSerialId = serial.Id,
            ProcessStepId = stationStep.Id,
            WorkStationId = station.Id,
            Result = "Fail",
            OperatorUserName = operatorUserName,
            OccurredAt = DateTimeOffset.UtcNow,
            Remark = remark
        });

        switch (disposition)
        {
            case "Isolate":
                serial.Status = ProcessStepStatus.Isolated;
                // 当前工序保留，放行后仍从该站继续（或放行时可指定）
                break;

            case "Scrap":
                await ApplyScrapAsync(serial, operatorUserName, station.Id, stationStep.Id, remark, writeFailRecord: false, ct);
                // Fail 记录已写；Scrap 再记一条 Disposition
                db.SerialPassRecords.Add(new SerialPassRecord
                {
                    Id = Guid.NewGuid(),
                    ProductSerialId = serial.Id,
                    ProcessStepId = stationStep.Id,
                    WorkStationId = station.Id,
                    Result = "Scrap",
                    OperatorUserName = operatorUserName,
                    OccurredAt = DateTimeOffset.UtcNow,
                    Remark = remark
                });
                break;

            case "Rework":
                await ApplyReworkAsync(serial, stationStep, reworkToSequence, operatorUserName, station.Id, remark, ct);
                break;
        }

        await db.SaveChangesAsync(ct);
        return serial;
    }

    /// <summary>隔离列表（班组长异常台）。</summary>
    public async Task<List<IsolatedSerialDto>> ListIsolatedAsync(CancellationToken ct = default)
    {
        var rows = await db.ProductSerials.AsNoTracking()
            .Include(s => s.WorkOrder)
            .Where(s => s.Status == ProcessStepStatus.Isolated)
            .ToListAsync(ct);

        var result = new List<IsolatedSerialDto>();
        foreach (var s in rows)
        {
            var stepCode = s.CurrentProcessStepId is null
                ? null
                : (await db.ProcessSteps.AsNoTracking().FirstOrDefaultAsync(p => p.Id == s.CurrentProcessStepId, ct))?.Code;
            result.Add(new IsolatedSerialDto(
                s.Id, s.SerialNo, s.WorkOrder!.OrderNo, s.WorkOrderId, stepCode, s.CreatedAt));
        }

        return result.OrderByDescending(x => x.CreatedAt).ToList();
    }

    /// <summary>
    /// 放行：Isolated → InProcess；可选指定回到某工序 Sequence，默认保持隔离前当前工序。
    /// </summary>
    public async Task<ProductSerial> ReleaseAsync(
        string serialNo,
        string reason,
        int? returnToSequence,
        string operatorUserName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("放行必须填写原因");
        }

        serialNo = serialNo.Trim().ToUpperInvariant();
        var serial = await db.ProductSerials
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct)
            ?? throw new InvalidOperationException("未找到该成品序列号");

        if (serial.Status != ProcessStepStatus.Isolated)
        {
            throw new InvalidOperationException("仅隔离中的序列号可以放行");
        }

        var routeId = serial.WorkOrder!.ProcessRouteId
            ?? throw new InvalidOperationException("生产工单未绑定工艺路线");

        ProcessStep? targetStep = null;
        if (returnToSequence is int seq)
        {
            targetStep = await db.ProcessSteps
                .FirstOrDefaultAsync(p => p.ProcessRouteId == routeId && p.Sequence == seq, ct)
                ?? throw new InvalidOperationException($"工艺路线上不存在顺序号为 {seq} 的工序");
            serial.CurrentProcessStepId = targetStep.Id;
        }
        else if (serial.CurrentProcessStepId is null)
        {
            // 极端：隔离时若无当前序，回到首序
            targetStep = await db.ProcessSteps.AsNoTracking()
                .Where(p => p.ProcessRouteId == routeId)
                .OrderBy(p => p.Sequence)
                .FirstAsync(ct);
            serial.CurrentProcessStepId = targetStep.Id;
        }
        else
        {
            targetStep = await db.ProcessSteps.AsNoTracking()
                .FirstAsync(p => p.Id == serial.CurrentProcessStepId, ct);
        }

        serial.Status = ProcessStepStatus.InProcess;

        db.SerialPassRecords.Add(new SerialPassRecord
        {
            Id = Guid.NewGuid(),
            ProductSerialId = serial.Id,
            ProcessStepId = targetStep!.Id,
            WorkStationId = null,
            Result = "Release",
            OperatorUserName = operatorUserName,
            OccurredAt = DateTimeOffset.UtcNow,
            Remark = reason.Trim()
        });

        await db.SaveChangesAsync(ct);
        return serial;
    }

    /// <summary>直接报废（含隔离中的 SN）。</summary>
    public async Task<ProductSerial> ScrapAsync(
        string serialNo,
        string? reason,
        string operatorUserName,
        Guid? workStationId,
        CancellationToken ct = default)
    {
        serialNo = serialNo.Trim().ToUpperInvariant();
        var serial = await db.ProductSerials
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct)
            ?? throw new InvalidOperationException("未找到该成品序列号");

        if (serial.Status == ProcessStepStatus.Scrapped)
        {
            throw new InvalidOperationException("该序列号已报废");
        }

        Guid stepId = serial.CurrentProcessStepId
            ?? await db.ProcessSteps.AsNoTracking()
                .Where(p => p.ProcessRouteId == serial.WorkOrder!.ProcessRouteId)
                .OrderBy(p => p.Sequence)
                .Select(p => p.Id)
                .FirstAsync(ct);

        var remark = string.IsNullOrWhiteSpace(reason) ? "Scrap" : reason.Trim();
        await ApplyScrapAsync(serial, operatorUserName, workStationId, stepId, remark, writeFailRecord: true, ct);
        await db.SaveChangesAsync(ct);
        return serial;
    }

    private async Task ApplyReworkAsync(
        ProductSerial serial,
        ProcessStep failStep,
        int? reworkToSequence,
        string operatorUserName,
        Guid stationId,
        string remark,
        CancellationToken ct)
    {
        var routeId = serial.WorkOrder!.ProcessRouteId
            ?? throw new InvalidOperationException("生产工单未绑定工艺路线");

        var targetSeq = reworkToSequence ?? failStep.ReworkToSequence ?? failStep.Sequence;
        var target = await db.ProcessSteps
            .FirstOrDefaultAsync(p => p.ProcessRouteId == routeId && p.Sequence == targetSeq, ct)
            ?? throw new InvalidOperationException($"返工目标顺序号 {targetSeq} 在工艺路线上不存在");

        serial.CurrentProcessStepId = target.Id;
        serial.Status = ProcessStepStatus.InProcess;

        db.SerialPassRecords.Add(new SerialPassRecord
        {
            Id = Guid.NewGuid(),
            ProductSerialId = serial.Id,
            ProcessStepId = target.Id,
            WorkStationId = stationId,
            Result = "Rework",
            OperatorUserName = operatorUserName,
            OccurredAt = DateTimeOffset.UtcNow,
            Remark = $"{remark} → step {target.Code}"
        });
    }

    private async Task ApplyScrapAsync(
        ProductSerial serial,
        string operatorUserName,
        Guid? workStationId,
        Guid processStepId,
        string remark,
        bool writeFailRecord,
        CancellationToken ct)
    {
        var wasCounting = serial.Status is ProcessStepStatus.InProcess
            or ProcessStepStatus.Isolated
            or ProcessStepStatus.RouteCompleted;

        serial.Status = ProcessStepStatus.Scrapped;
        serial.CurrentProcessStepId = null;

        var wo = await db.WorkOrders.FirstAsync(w => w.Id == serial.WorkOrderId, ct);
        wo.ScrappedQty += 1;
        if (wasCounting && wo.InProcessSerialCount > 0)
        {
            wo.InProcessSerialCount -= 1;
        }

        if (writeFailRecord)
        {
            db.SerialPassRecords.Add(new SerialPassRecord
            {
                Id = Guid.NewGuid(),
                ProductSerialId = serial.Id,
                ProcessStepId = processStepId,
                WorkStationId = workStationId,
                Result = "Scrap",
                OperatorUserName = operatorUserName,
                OccurredAt = DateTimeOffset.UtcNow,
                Remark = remark
            });
        }

        await Task.CompletedTask;
    }
}

public record IsolatedSerialDto(
    Guid Id,
    string SerialNo,
    string WorkOrderNo,
    Guid WorkOrderId,
    string? CurrentStepCode,
    DateTimeOffset CreatedAt);
