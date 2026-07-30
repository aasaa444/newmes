using Mes.Infrastructure.Seeding;

namespace Mes.Database.Tests;

/// <summary>证明演示数据只能在非生产环境且经过显式确认后装载。</summary>
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
