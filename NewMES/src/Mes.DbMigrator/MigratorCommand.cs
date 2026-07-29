namespace Mes.DbMigrator;

public enum MigratorOperation
{
    Migrate,
    SeedDemo,
    BootstrapAdministrator,
}

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
