using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

/// <summary>
/// 开发期数据库启动逻辑。
/// EF EnsureCreated：库已存在时不补新表；探测 Materials / WorkOrders，缺失则 Development 下重建。
/// 正式试点应改为 Migration。
/// </summary>
public static class DatabaseBootstrap
{
    public static void Initialize(MesDbContext db, IHostEnvironment env, ILogger logger)
    {
        db.Database.EnsureCreated();

        if (!SchemaLooksCurrent(db))
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "数据库 schema 落后（缺 Materials 或 WorkOrders 等）。请迁移或重建 MesDb。");
            }

            logger.LogWarning(
                "MesDb schema is outdated. Recreating database (Development/Testing only).");
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }

        IdentitySeed.EnsureSeeded(db);
        MasterDataSeed.EnsureSeeded(db);
        InventorySeed.EnsureSeeded(db);
    }

    /// <summary>票 02+03 的粗粒度 schema 探测。</summary>
    private static bool SchemaLooksCurrent(MesDbContext db)
    {
        try
        {
            _ = db.Materials.AsNoTracking().Any();
            _ = db.WorkOrders.AsNoTracking().Any();
            _ = db.LineSideInventories.AsNoTracking().Any();
            _ = db.ProductSerials.AsNoTracking().Any();
            _ = db.FinishedGoodsInventories.AsNoTracking().Any();
            _ = db.ErpOutboxMessages.AsNoTracking().Any();
            // 触发 Users 新列（CanViewOpsOverview）探测
            _ = db.Users.AsNoTracking().Select(u => u.CanViewOpsOverview).Take(1).ToList();
            return true;
        }
        catch (Exception ex) when (IsMissingTable(ex))
        {
            return false;
        }
    }

    private static bool IsMissingTable(Exception ex)
    {
        // SQL Server: Invalid object name → 208
        for (var e = ex; e != null; e = e.InnerException!)
        {
            // 208 缺表；207 缺列（如 CanViewOpsOverview）
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 208 or 207)
            {
                return true;
            }

            // SQLite 测试库 / SQL Server 缺列（二期加 CanViewOpsOverview 等）
            if (e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
