namespace Mes.Infrastructure.Seeding;

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
