using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mes.Infrastructure.Persistence;

/// <summary>供 EF 设计时命令显式创建上下文；连接串必须由环境提供，避免误连隐式数据库。</summary>
public sealed class MesDbContextDesignFactory : IDesignTimeDbContextFactory<MesDbContext>
{
    public MesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MesDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__MesDatabase is required for design-time database operations.");
        }
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MesDbContext(options);
    }
}
