using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Seeding;
using Mes.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.DbMigrator;

/// <summary>
/// 独立数据库运维进程。它使用高于 API 的迁移权限执行受控升级或一次性引导，完成后立即退出。
/// </summary>
public static class MigratorApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = MigratorCommand.Parse(args);
            var secretsDirectory = Environment.GetEnvironmentVariable("MES_SECRETS_DIRECTORY")
                ?? "/run/secrets";
            var connectionString = Environment.GetEnvironmentVariable(
                "ConnectionStrings__MesDatabase")
                ?? ExternalSecretFile.ReadOptional(
                    secretsDirectory,
                    "ConnectionStrings__MesDatabase");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine(
                    "ConnectionStrings__MesDatabase is required; no implicit database is used.");
                return 3;
            }

            var options = new DbContextOptionsBuilder<MesDbContext>()
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .Options;
            await using var context = new MesDbContext(options);

            // API 身份没有 DDL 权限，所有架构变化必须通过这个显式迁移分支完成。
            if (command.Operation == MigratorOperation.Migrate)
            {
                Console.WriteLine("Applying controlled EF Core migrations...");
                await context.Database.MigrateAsync();
                Console.WriteLine(
                    $"Database is current at {MesMigrationIds.IdempotentProductionOrderIngress}.");
                return 0;
            }

            // 首个管理员密码只从进程环境或挂载秘密读取，不进入命令行、日志或应用配置文件。
            if (command.Operation == MigratorOperation.BootstrapAdministrator)
            {
                var password = Environment.GetEnvironmentVariable("InitialAdmin__Password")
                    ?? ExternalSecretFile.ReadOptional(
                        secretsDirectory,
                        "InitialAdmin__Password");
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentException(
                        "InitialAdmin__Password must be supplied through an external secret.");
                }

                var bootstrapper = new InitialAdministratorBootstrapper(
                    context,
                    new PasswordHasher<UserAccount>(),
                    TimeProvider.System);
                await bootstrapper.BootstrapAsync(
                    command.Username!,
                    command.DisplayName!,
                    password,
                    $"bootstrap-admin-{Guid.NewGuid():N}");
                Console.WriteLine(
                    $"Initial administrator '{command.Username}' was provisioned.");
                return 0;
            }

            var environmentName =
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Production";
            // 演示数据必须同时满足非生产环境与操作者显式确认。
            DemoSeedPolicy.EnsureAllowed(
                environmentName,
                command.ConfirmNonProduction);
            await new DemoDataInitializer(context).LoadAsync();
            Console.WriteLine("Explicit non-production demo data load completed.");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Database operation failed and readiness must remain blocked: {exception.Message}");
            return 4;
        }
    }
}
