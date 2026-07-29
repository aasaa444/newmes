using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Mes.SqlServer.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string MasterConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"NewMesTests_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();

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
