using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

/// <summary>API 启动门禁：只读校验连接与 Migration，不创建、升级或重建数据库。</summary>
public static class DatabaseCompatibilityVerifier
{
    public static async Task EnsureCompatibleAsync(
        MesDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "MES 数据库不存在或无法连接。请先执行 'database migrate'，不要依赖 API 启动建库。");
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length > 0)
        {
            throw new InvalidOperationException(
                $"MES 数据库存在 {pending.Length} 个待应用 Migration：{string.Join(", ", pending)}。" +
                "请在启动 API 前执行 'database migrate'。当前启动不会修改数据库结构。");
        }
    }
}
