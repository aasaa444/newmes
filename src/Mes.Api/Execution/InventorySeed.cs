using Mes.Api.Data;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 线边库存演示种子：保证路由器 BOM 组件足够领一笔演示工单（如 10 台）。
/// 依赖 MasterDataSeed 已存在物料编码。
/// </summary>
public static class InventorySeed
{
    /// <summary>演示默认可支撑的计划台数量级。</summary>
    public const decimal DemoSupportQty = 100;

    public static void EnsureSeeded(MesDbContext db)
    {
        EnsureLineSide(db, MasterDataSeed.PcbCode, DemoSupportQty);
        EnsureLineSide(db, MasterDataSeed.PsuCode, DemoSupportQty);
        EnsureLineSide(db, MasterDataSeed.ScrewCode, DemoSupportQty * 4); // BOM 每台 4 颗
    }

    private static void EnsureLineSide(MesDbContext db, string materialCode, decimal minQty)
    {
        var material = db.Materials.AsNoTracking().FirstOrDefault(m => m.Code == materialCode);
        if (material is null)
        {
            return;
        }

        var row = db.LineSideInventories.FirstOrDefault(x => x.MaterialId == material.Id);
        if (row is null)
        {
            db.LineSideInventories.Add(new LineSideInventory
            {
                Id = Guid.NewGuid(),
                MaterialId = material.Id,
                QuantityOnHand = minQty
            });
            db.SaveChanges();
            return;
        }

        if (row.QuantityOnHand < minQty)
        {
            row.QuantityOnHand = minQty;
            db.SaveChanges();
        }
    }
}
