using Mes.DbMigrator;

namespace Mes.Database.Tests;

/// <summary>证明迁移器只接受无歧义的受控命令，危险或不完整参数会失败关闭。</summary>
public sealed class MigratorCommandTests
{
    [Theory]
    [InlineData("migrate", MigratorOperation.Migrate)]
    [InlineData("seed-demo", MigratorOperation.SeedDemo)]
    public void ParseAcceptsControlledDatabaseOperationsWithoutArguments(
        string argument,
        MigratorOperation expected)
    {
        var command = MigratorCommand.Parse([argument]);

        Assert.Equal(expected, command.Operation);
    }

    [Fact]
    public void ParseAcceptsExplicitInitialAdministratorBootstrap()
    {
        var command = MigratorCommand.Parse(
            ["bootstrap-admin", "--username", "mes.admin", "--display-name", "MES Administrator"]);

        Assert.Equal(MigratorOperation.BootstrapAdministrator, command.Operation);
        Assert.Equal("mes.admin", command.Username);
        Assert.Equal("MES Administrator", command.DisplayName);
    }

    [Fact]
    public void ParseRejectsUnknownOrImplicitOperations()
    {
        var error = Assert.Throws<ArgumentException>(() => MigratorCommand.Parse([]));

        Assert.Contains("migrate", error.Message, StringComparison.Ordinal);
        Assert.Contains("seed-demo", error.Message, StringComparison.Ordinal);
        Assert.Contains("bootstrap-admin", error.Message, StringComparison.Ordinal);
    }
}
