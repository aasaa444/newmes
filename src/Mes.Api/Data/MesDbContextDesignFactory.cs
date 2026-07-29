using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mes.Api.Data;

/// <summary>供 EF 工具生成 Migration；运行时仍使用应用配置中的连接字符串。</summary>
public sealed class MesDbContextDesignFactory : IDesignTimeDbContextFactory<MesDbContext>
{
    public MesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MesDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=MesDb;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MesDbContext(options);
    }
}
