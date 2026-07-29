using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mes.Api.Data;

/// <summary>
/// 将旧版 EnsureCreated 数据库登记为初始 Migration 基线。
/// 该逻辑只由显式 migrate 命令调用，并要求结构与当前已知 Legacy 模型完全一致。
/// </summary>
public static class LegacyDatabaseAdopter
{
    public static async Task<bool> TryAdoptAsync(
        MesDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            return false;
        }

        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        if (await HistoryTableExistsAsync(connection, cancellationToken))
        {
            return false;
        }

        var actualTables = await ReadActualTablesAsync(connection, cancellationToken);
        if (actualTables.Count == 0)
        {
            return false;
        }

        var expectedTables = ReadExpectedTables(db);
        var actualColumns = await ReadActualColumnsAsync(connection, cancellationToken);
        var expectedColumns = ReadExpectedColumns(db);
        var actualConstraints = await ReadActualConstraintsAsync(connection, cancellationToken);
        var expectedConstraints = ReadExpectedConstraints(db);

        var differences = new List<string>();
        AddDifferences("表", expectedTables, actualTables, differences);
        AddDifferences("列", expectedColumns, actualColumns, differences);
        AddDifferences("约束/索引", expectedConstraints, actualConstraints, differences);
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                "检测到无 Migration 历史的数据库，但它不匹配已知 Legacy 基线；为避免错误认领，未修改数据库。" +
                Environment.NewLine + string.Join(Environment.NewLine, differences.Take(20)));
        }

        var initialMigration = db.Database.GetMigrations().FirstOrDefault()
            ?? throw new InvalidOperationException("应用没有可登记的初始 Migration。");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            CREATE TABLE [dbo].[__EFMigrationsHistory] (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
            );
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (@migrationId, @productVersion);
            """;
        AddParameter(command, "@migrationId", initialMigration);
        AddParameter(command, "@productVersion", ProductInfo.GetVersion());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogWarning(
            "已将结构完全匹配的旧 EnsureCreated 数据库登记为 Migration 基线 {Migration}。" +
            "执行前应已完成数据库备份；原业务数据未被改写。",
            initialMigration);
        return true;
    }

    private static HashSet<string> ReadExpectedTables(MesDbContext db) =>
        db.Model.GetRelationalModel().Tables
            .Select(t => Key(t.Schema ?? "dbo", t.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ReadExpectedColumns(MesDbContext db)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            foreach (var column in table.Columns)
            {
                var isIdentity = column.PropertyMappings.Any(mapping =>
                    mapping.Property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn);
                result.Add(Key(
                    table.Schema ?? "dbo",
                    table.Name,
                    column.Name,
                    NormalizeStoreType(column.StoreType),
                    column.IsNullable ? "nullable" : "required",
                    isIdentity ? "identity" : "plain"));
            }
        }
        return result;
    }

    private static HashSet<string> ReadExpectedConstraints(MesDbContext db)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            var schema = table.Schema ?? "dbo";
            foreach (var constraint in table.UniqueConstraints)
            {
                result.Add(Key(schema, table.Name, "key", constraint.Name));
            }
            foreach (var index in table.Indexes)
            {
                result.Add(Key(schema, table.Name, "index", index.Name));
            }
            foreach (var foreignKey in table.ForeignKeyConstraints)
            {
                result.Add(Key(schema, table.Name, "foreign-key", foreignKey.Name));
            }
        }
        return result;
    }

    private static async Task<HashSet<string>> ReadActualTablesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name, t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND t.name <> '__EFMigrationsHistory';
            """;
        return await ReadKeysAsync(command, 2, cancellationToken);
    }

    private static async Task<HashSet<string>> ReadActualColumnsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name, t.name, c.name,
                CASE
                    WHEN ty.name IN ('nvarchar', 'nchar') THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length / 2) END + ')'
                    WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary') THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length) END + ')'
                    WHEN ty.name IN ('decimal', 'numeric') THEN ty.name + '(' + CONVERT(varchar(10), c.precision) + ',' + CONVERT(varchar(10), c.scale) + ')'
                    ELSE ty.name
                END AS store_type,
                CASE WHEN c.is_nullable = 1 THEN 'nullable' ELSE 'required' END,
                CASE WHEN c.is_identity = 1 THEN 'identity' ELSE 'plain' END
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0 AND t.name <> '__EFMigrationsHistory';
            """;
        return await ReadKeysAsync(command, 6, cancellationToken);
    }

    private static async Task<HashSet<string>> ReadActualConstraintsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name, t.name, 'key', kc.name
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT s.name, t.name, 'index', i.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
              AND i.name IS NOT NULL
              AND i.is_primary_key = 0
              AND i.is_unique_constraint = 0
            UNION ALL
            SELECT s.name, t.name, 'foreign-key', fk.name
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON t.object_id = fk.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0;
            """;
        return await ReadKeysAsync(command, 4, cancellationToken);
    }

    private static async Task<HashSet<string>> ReadKeysAsync(
        DbCommand command,
        int fieldCount,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = Enumerable.Range(0, fieldCount)
                .Select(index => reader.GetString(index))
                .ToArray();
            result.Add(Key(values));
        }
        return result;
    }

    private static async Task<bool> HistoryTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL THEN 0 ELSE 1 END;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static void AddDifferences(
        string category,
        HashSet<string> expected,
        HashSet<string> actual,
        List<string> differences)
    {
        differences.AddRange(expected.Except(actual).Order().Select(value => $"缺少{category}: {value}"));
        differences.AddRange(actual.Except(expected).Order().Select(value => $"多余{category}: {value}"));
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeStoreType(string storeType) => storeType.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string Key(params string[] values) => string.Join('|', values).ToLowerInvariant();
}
