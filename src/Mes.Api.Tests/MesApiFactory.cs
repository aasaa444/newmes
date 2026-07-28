using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mes.Api.Data;
using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>
/// 集成测试宿主：把真实 API 的 SQL Server 换成 SQLite 内存库。
/// 环境名 Testing → Program 不注册 SQL Server、不跑 DatabaseBootstrap。
/// 连接必须 Open 并保持，否则 :memory: 库会随连接关闭而消失。
/// </summary>
public class MesApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 去掉 Program 里可能残留的登记，换成测试库
            services.RemoveAll(typeof(DbContextOptions<MesDbContext>));
            services.RemoveAll(typeof(MesDbContext));

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<MesDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // 建表 + 身份/主数据种子（与生产启动等价，但不走删库逻辑）
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            db.Database.EnsureCreated();
            IdentitySeed.EnsureSeeded(db);
            MasterDataSeed.EnsureSeeded(db);
            InventorySeed.EnsureSeeded(db);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}
