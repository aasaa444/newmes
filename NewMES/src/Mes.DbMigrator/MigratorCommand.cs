namespace Mes.DbMigrator;

// 数据库结构迁移、演示数据和首个管理员初始化是三个显式操作，避免部署脚本误触发额外副作用。
public enum MigratorOperation
{
    Migrate,
    SeedDemo,
    BootstrapAdministrator,
}

/// <summary>严格解析迁移工具参数；未知参数直接失败，生产自动化不能依赖宽松猜测。</summary>
public sealed record MigratorCommand(
    MigratorOperation Operation,
    bool ConfirmNonProduction,
    string? Username = null,
    string? DisplayName = null)
{
    private const string Usage =
        "Expected 'migrate', 'seed-demo --confirm-non-production', or "
        + "'bootstrap-admin --username <username> --display-name <display name>'.";

    public static MigratorCommand Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new ArgumentException(Usage, nameof(arguments));
        }

        return arguments[0].ToLowerInvariant() switch
        {
            "migrate" when arguments.Count == 1 =>
                new MigratorCommand(MigratorOperation.Migrate, false),
            "seed-demo" when arguments.Count == 1 =>
                new MigratorCommand(MigratorOperation.SeedDemo, false),
            "seed-demo" when arguments.Count == 2
                && arguments[1] == "--confirm-non-production" =>
                new MigratorCommand(
                    MigratorOperation.SeedDemo,
                    true),
            "bootstrap-admin" when arguments.Count == 5
                && arguments[1] == "--username"
                && !string.IsNullOrWhiteSpace(arguments[2])
                && arguments[3] == "--display-name"
                && !string.IsNullOrWhiteSpace(arguments[4]) =>
                new MigratorCommand(
                    MigratorOperation.BootstrapAdministrator,
                    false,
                    arguments[2].Trim(),
                    arguments[4].Trim()),
            _ => throw new ArgumentException(Usage, nameof(arguments)),
        };
    }
}
