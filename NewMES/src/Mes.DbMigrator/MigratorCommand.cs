namespace Mes.DbMigrator;

public enum MigratorOperation
{
    Migrate,
    SeedDemo,
}

public sealed record MigratorCommand(MigratorOperation Operation, bool ConfirmNonProduction)
{
    private const string Usage =
        "Expected 'migrate' or 'seed-demo --confirm-non-production'.";

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
            "seed-demo" when arguments.Count <= 2 =>
                new MigratorCommand(
                    MigratorOperation.SeedDemo,
                    arguments.Skip(1).SingleOrDefault()
                        is "--confirm-non-production"),
            _ => throw new ArgumentException(Usage, nameof(arguments)),
        };
    }
}
