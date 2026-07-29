namespace Mes.SqlServer.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("NEWMES_RUN_SQLSERVER_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set NEWMES_RUN_SQLSERVER_TESTS=true to run the real SQL Server release gate.";
        }
    }
}
