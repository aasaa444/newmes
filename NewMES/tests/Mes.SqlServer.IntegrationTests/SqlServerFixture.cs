using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Mes.SqlServer.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly ConcurrentBag<string> _createdDatabases = [];
    private readonly MsSqlContainer? _container;
    private readonly string? _externalConnectionString;
    private readonly bool _isGateEnabled;

    public SqlServerFixture()
    {
        _isGateEnabled = string.Equals(
            Environment.GetEnvironmentVariable("NEWMES_RUN_SQLSERVER_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (!_isGateEnabled)
        {
            return;
        }

        var configuredConnection = Environment.GetEnvironmentVariable(
            "NEWMES_SQLSERVER_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(configuredConnection))
        {
            var builder = new SqlConnectionStringBuilder(configuredConnection)
            {
                InitialCatalog = "master",
            };
            _externalConnectionString = builder.ConnectionString;
            return;
        }

        _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-latest").Build();
    }

    public string MasterConnectionString =>
        _externalConnectionString
        ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The SQL Server gate is not enabled.");

    public Task InitializeAsync() =>
        !_isGateEnabled
            ? Task.CompletedTask
            : _container?.StartAsync() ?? Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_isGateEnabled)
        {
            return;
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        foreach (var databaseName in _createdDatabases)
        {
            if (!databaseName.StartsWith("NewMesTests_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to clean a database outside the NewMES test namespace.");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $$"""
                ALTER DATABASE [{{databaseName}}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{{databaseName}}];
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"NewMesTests_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        _createdDatabases.Add(databaseName);

        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName,
        };

        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerFixtureProvider : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
