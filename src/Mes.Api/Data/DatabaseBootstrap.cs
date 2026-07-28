using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

/// <summary>
/// 开发期数据库启动逻辑。
/// EF 的 EnsureCreated()：仅当「整个库不存在」时建表；库已存在（例如只跑过票 01）时
/// 不会自动补 Materials 等新表，因此这里探测主数据表，缺失则在 Development 下删库重建。
/// 正式试点应改为 Migration，避免 EnsureDeleted。
/// </summary>
public static class DatabaseBootstrap
{
    public static void Initialize(MesDbContext db, IHostEnvironment env, ILogger logger)
    {
        // 先尝试按当前模型建库（空实例时会建全表）
        db.Database.EnsureCreated();

        if (!MasterDataTablesExist(db))
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "数据库已存在但缺少主数据表（Materials 等）。请迁移或重建 MesDb。");
            }

            logger.LogWarning(
                "MesDb schema is outdated (missing Materials). Recreating database (Development/Testing only).");
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }

        IdentitySeed.EnsureSeeded(db);
        MasterDataSeed.EnsureSeeded(db);
    }

    /// <summary>探测 Materials 表是否存在（票 02 起的 schema 标志）。</summary>
    private static bool MasterDataTablesExist(MesDbContext db)
    {
        try
        {
            // 任意轻量查询：表不存在会抛 SqlException 208
            _ = db.Materials.AsNoTracking().Any();
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
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 208)
            {
                return true;
            }

            // SQLite 测试库：no such table
            if (e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
