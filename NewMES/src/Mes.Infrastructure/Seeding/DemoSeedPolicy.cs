namespace Mes.Infrastructure.Seeding;

/// <summary>要求“非生产环境 + 显式确认”双重条件，防止演示数据因误用命令进入生产库。</summary>
public static class DemoSeedPolicy
{
    public static void EnsureAllowed(string environmentName, bool isConfirmed)
    {
        if (string.Equals(
                environmentName,
                "Production",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Demo data is disabled in the Production environment.");
        }

        if (!isConfirmed)
        {
            throw new InvalidOperationException(
                "Demo data requires --confirm-non-production.");
        }
    }
}
