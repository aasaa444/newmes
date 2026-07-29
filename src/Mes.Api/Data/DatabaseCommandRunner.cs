using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

/// <summary>
/// 显式数据库运维入口。数据库结构和演示数据不再由 Web API 启动隐式修改。
/// </summary>
public static class DatabaseCommandRunner
{
    public static bool IsDatabaseCommand(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], "database", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        string[] args,
        IServiceProvider services,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Mes.Database");
        if (args.Length != 2)
        {
            logger.LogError("用法: database migrate | database seed-demo | database status");
            return 2;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            switch (args[1].ToLowerInvariant())
            {
                case "migrate":
                    await LegacyDatabaseAdopter.TryAdoptAsync(db, logger, cancellationToken);
                    await db.Database.MigrateAsync(cancellationToken);
                    logger.LogInformation("数据库 Migration 已全部应用。");
                    return 0;

                case "seed-demo":
                    if (environment.IsProduction())
                    {
                        logger.LogError("正式环境禁止装载演示账号、主数据和库存。");
                        return 3;
                    }

                    await DatabaseCompatibilityVerifier.EnsureCompatibleAsync(db, cancellationToken);
                    await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
                    {
                        IdentitySeed.EnsureSeeded(db);
                        MasterDataSeed.EnsureSeeded(db);
                        InventorySeed.EnsureSeeded(db);
                        await transaction.CommitAsync(cancellationToken);
                    }

                    logger.LogInformation("非生产演示账号、主数据和库存已显式装载。");
                    return 0;

                case "status":
                    await DatabaseCompatibilityVerifier.EnsureCompatibleAsync(db, cancellationToken);
                    logger.LogInformation("数据库版本与应用兼容。");
                    return 0;

                default:
                    logger.LogError("未知数据库命令 {Command}。用法: database migrate | database seed-demo | database status", args[1]);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "数据库命令 {Command} 失败；未启动 Web API。", string.Join(' ', args));
            return 1;
        }
    }
}
