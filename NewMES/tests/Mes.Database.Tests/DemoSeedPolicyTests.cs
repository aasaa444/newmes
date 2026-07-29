using Mes.Infrastructure.Seeding;

namespace Mes.Database.Tests;

public sealed class DemoSeedPolicyTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("production", true)]
    [InlineData("Development", false)]
    public void DemoSeedIsRejectedWithoutBothNonProductionAndExplicitConfirmation(
        string environment,
        bool isConfirmed)
    {
        Assert.Throws<InvalidOperationException>(
            () => DemoSeedPolicy.EnsureAllowed(environment, isConfirmed));
    }

    [Fact]
    public void DemoSeedIsAllowedOnlyWhenNonProductionIsExplicitlyConfirmed()
    {
        DemoSeedPolicy.EnsureAllowed("Development", isConfirmed: true);
    }
}
