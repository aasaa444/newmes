namespace Mes.SqlServer.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
/// <summary>只有显式打开真实 SQL Server 门禁时才运行集成事实，避免普通单元测试误报数据库证据。</summary>
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
