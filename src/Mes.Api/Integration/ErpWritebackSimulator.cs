using System.Text.Json;
using Mes.Api.Data;
using Mes.Api.Execution;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Integration;

/// <summary>
/// ERP 回写模拟器（ADR-0005）：生成可查询出站报文，不调用真实用友/金蝶。
/// 字段命名贴近国内 ERP 制造回写习惯（单据号、物料编码、数量、仓库、时间）。
/// </summary>
public class ErpWritebackSimulator(MesDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>领料/耗料回写（工单发料）。</summary>
    public async Task EnqueueMaterialIssueAsync(WorkOrder wo, IReadOnlyList<WorkOrderIssueLine> lines, CancellationToken ct = default)
    {
        var mats = await db.Materials.AsNoTracking()
            .Where(m => lines.Select(l => l.MaterialId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var payload = new
        {
            // 用友/金蝶风格：生产发料单
            docType = "MO_Issue",
            sourceSystem = "MES",
            targetHint = "YonyouOrKingdee",
            moNumber = wo.OrderNo,
            orgCode = "100",
            warehouseCode = "LINE_SIDE",
            billDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            entries = lines.Select(l => new
            {
                materialCode = mats.GetValueOrDefault(l.MaterialId)?.Code ?? l.MaterialId.ToString(),
                materialName = mats.GetValueOrDefault(l.MaterialId)?.Name,
                qty = l.IssuedQty,
                stockStatus = "Available",
                remark = mats.GetValueOrDefault(l.MaterialId)?.IsKeyComponent == true ? "KeyComponentPendingBind" : "ConsumedOnIssue"
            }).ToList()
        };

        await SaveAsync("MaterialIssue", wo.OrderNo, payload, ct);
    }

    /// <summary>完工入库回写（生产入库单）。</summary>
    public async Task EnqueueProductionReceiptAsync(
        WorkOrder wo,
        string serialNo,
        string finishedMaterialCode,
        decimal qty,
        CancellationToken ct = default)
    {
        var payload = new
        {
            docType = "MO_Receipt",
            sourceSystem = "MES",
            targetHint = "YonyouOrKingdee",
            moNumber = wo.OrderNo,
            orgCode = "100",
            warehouseCode = "FG",
            billDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            serialNo,
            entries = new[]
            {
                new
                {
                    materialCode = finishedMaterialCode,
                    qty,
                    stockStatus = "Available",
                    qualityStatus = "Qualified"
                }
            }
        };

        await SaveAsync("ProductionReceipt", $"{wo.OrderNo}:{serialNo}", payload, ct);
    }

    /// <summary>工单关闭回写（可选）。</summary>
    public async Task EnqueueWorkOrderCloseAsync(WorkOrder wo, CancellationToken ct = default)
    {
        var payload = new
        {
            docType = "MO_Close",
            sourceSystem = "MES",
            targetHint = "YonyouOrKingdee",
            moNumber = wo.OrderNo,
            plannedQty = wo.PlannedQty,
            completedQty = wo.CompletedQty,
            scrappedQty = wo.ScrappedQty,
            closeTime = DateTime.UtcNow.ToString("o")
        };
        await SaveAsync("WorkOrderClose", wo.OrderNo, payload, ct);
    }

    public async Task<List<ErpOutboxMessage>> ListRecentAsync(int take = 100, CancellationToken ct = default)
    {
        var raw = await db.ErpOutboxMessages.AsNoTracking().ToListAsync(ct);
        return raw.OrderByDescending(m => m.CreatedAt).Take(take).ToList();
    }

    private async Task SaveAsync(string type, string? businessKey, object payload, CancellationToken ct)
    {
        db.ErpOutboxMessages.Add(new ErpOutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = type,
            BusinessKey = businessKey,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOpts),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "SimulatedSent"
        });
        await db.SaveChangesAsync(ct);
    }
}
