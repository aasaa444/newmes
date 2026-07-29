using Mes.DbMigrator;

namespace Mes.Database.Tests;

public sealed class MigratorCommandTests
{
    [Theory]
    [InlineData("migrate", MigratorOperation.Migrate)]
    [InlineData("seed-demo", MigratorOperation.SeedDemo)]
    public void ParseAcceptsTheTwoControlledDatabaseOperations(
        string argument,
        MigratorOperation expected)
    {
        var command = MigratorCommand.Parse([argument]);

        Assert.Equal(expected, command.Operation);
    }

    [Fact]
    public void ParseRejectsUnknownOrImplicitOperations()
    {
        var error = Assert.Throws<ArgumentException>(() => MigratorCommand.Parse([]));

        Assert.Contains("migrate", error.Message, StringComparison.Ordinal);
        Assert.Contains("seed-demo", error.Message, StringComparison.Ordinal);
    }
}
