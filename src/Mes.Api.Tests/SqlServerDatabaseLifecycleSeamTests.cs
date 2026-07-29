using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using FluentAssertions;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Tests;

/// <summary>
/// 票 01 的真实 SQL Server 接缝：只观察数据库命令退出结果与 API 的公开 HTTP 行为。
/// 默认使用 Windows LocalDB；CI 可用 MES_SQLSERVER_TEST_CONNECTION 指向临时 SQL Server。
/// </summary>
public sealed class SqlServerDatabaseLifecycleSeamTests : IAsyncLifetime
{
    private readonly string _databaseName = $"MesTicket01_{Guid.NewGuid():N}";
    private readonly string _serverConnectionString;
    private readonly string _databaseConnectionString;

    public SqlServerDatabaseLifecycleSeamTests()
    {
        var configured = Environment.GetEnvironmentVariable("MES_SQLSERVER_TEST_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";
        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master"
        };
        _serverConnectionString = builder.ConnectionString;
        builder.InitialCatalog = _databaseName;
        _databaseConnectionString = builder.ConnectionString;
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Empty_database_can_be_migrated_seeded_and_started()
    {
        await AssertSqlServerAvailableAsync();

        var migrate = await RunDatabaseCommandAsync("migrate");
        migrate.ExitCode.Should().Be(0, migrate.Output);

        var seed = await RunDatabaseCommandAsync("seed-demo");
        seed.ExitCode.Should().Be(0, seed.Output);

        var port = GetFreeTcpPort();
        await using var api = StartApi(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        await WaitUntilHealthyAsync(client, api);
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "planner",
            password = "Planner@123"
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Legacy_database_with_data_can_be_adopted_without_losing_users()
    {
        await AssertSqlServerAvailableAsync();
        await CreateLegacyDatabaseAsync();

        var migrate = await RunDatabaseCommandAsync("migrate");
        migrate.ExitCode.Should().Be(0, migrate.Output);

        var port = GetFreeTcpPort();
        await using var api = StartApi(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await WaitUntilHealthyAsync(client, api);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "legacy-planner",
            password = "Legacy@123"
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Unmigrated_database_is_rejected_without_being_created_by_api_startup()
    {
        await AssertSqlServerAvailableAsync();

        var port = GetFreeTcpPort();
        await using var api = StartApi(port);
        await api.WaitForExitAsync(TimeSpan.FromSeconds(20));
        var output = await api.ReadOutputAsync();

        api.ExitCode.Should().NotBe(0);
        output.Should().Contain("database migrate");
        (await DatabaseExistsAsync()).Should().BeFalse("API 启动兼容性检查不得创建数据库");
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Migration_is_repeatable_and_failed_demo_seed_rolls_back_all_seed_changes()
    {
        await AssertSqlServerAvailableAsync();
        (await RunDatabaseCommandAsync("migrate")).ExitCode.Should().Be(0);
        var repeated = await RunDatabaseCommandAsync("migrate");
        repeated.ExitCode.Should().Be(0, repeated.Output);

        // 制造一个主数据唯一键冲突，让 seed-demo 在账号写入后失败，验证整笔显式初始化回滚。
        await ExecuteDatabaseSqlAsync("""
            INSERT INTO [Materials]
                ([Id], [Code], [Name], [IsFinishedGood], [IsKeyComponent], [RequiresSerialNumber], [IsActive])
            VALUES
                (NEWID(), N'PCB-MAIN', N'冲突夹具', 0, 1, 1, 1);
            """);

        var seed = await RunDatabaseCommandAsync("seed-demo");
        seed.ExitCode.Should().NotBe(0);

        var port = GetFreeTcpPort();
        await using var api = StartApi(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await WaitUntilHealthyAsync(client, api);
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "planner",
            password = "Planner@123"
        });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "失败的演示初始化必须回滚先写入的演示账号");
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Production_environment_rejects_demo_seed()
    {
        await AssertSqlServerAvailableAsync();
        (await RunDatabaseCommandAsync("migrate")).ExitCode.Should().Be(0);

        var seed = await RunDatabaseCommandAsync("seed-demo", "Production");

        seed.ExitCode.Should().Be(3, seed.Output);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertSqlServerAvailableAsync()
    {
        try
        {
            await using var connection = new SqlConnection(_serverConnectionString);
            await connection.OpenAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "真实 SQL Server 测试不可用。请启动 LocalDB，或设置 MES_SQLSERVER_TEST_CONNECTION。",
                ex);
        }
    }

    private async Task CreateLegacyDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(_databaseConnectionString)
            .Options;
        await using var db = new MesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserAccount
        {
            Id = Guid.NewGuid(),
            UserName = "legacy-planner",
            DisplayName = "旧版本计划员",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Legacy@123"),
            Role = AppRoles.Planner,
            IsActive = true,
            CanViewOpsOverview = true
        });
        await db.SaveChangesAsync();
    }

    private async Task<ProcessResult> RunDatabaseCommandAsync(
        string command,
        string environment = "Development")
    {
        using var process = CreateProcess(environment, "database", command);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new ProcessResult(-1, $"数据库命令超时，可能错误启动了 Web API。{Environment.NewLine}{await stdout}{await stderr}");
        }

        return new ProcessResult(process.ExitCode, await stdout + await stderr);
    }

    private RunningApi StartApi(int port)
    {
        var process = CreateProcess("Development", "--urls", $"http://127.0.0.1:{port}");
        process.Start();
        return new RunningApi(process);
    }

    private Process CreateProcess(string environment, params string[] arguments)
    {
        var apiAssembly = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(apiAssembly)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(apiAssembly);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__MesDb"] = _databaseConnectionString;
        return new Process { StartInfo = start };
    }

    private async Task<bool> DatabaseExistsAsync()
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@databaseName", _databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private async Task ExecuteDatabaseSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(_databaseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilHealthyAsync(HttpClient client, RunningApi api)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            if (api.HasExited)
            {
                throw new InvalidOperationException($"API 在就绪前退出。{Environment.NewLine}{await api.ReadOutputAsync()}");
            }

            try
            {
                var response = await client.GetAsync("/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
                lastError = new InvalidOperationException($"健康检查返回 {(int)response.StatusCode}。");
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"API 未在 30 秒内就绪：{lastError?.Message}{Environment.NewLine}{await api.ReadOutputAsync()}");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class RunningApi(Process process) : IAsyncDisposable
    {
        private readonly Task<string> _stdout = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();

        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public async Task WaitForExitAsync(TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
        }

        public async Task<string> ReadOutputAsync() => await _stdout + await _stderr;

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            process.Dispose();
        }
    }
}
