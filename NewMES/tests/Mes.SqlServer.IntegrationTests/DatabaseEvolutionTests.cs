using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Seeding;
using Mes.Domain.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Globalization;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class DatabaseEvolutionTests(SqlServerFixture server)
{
    [SqlServerFact]
    public async Task EmptyDatabaseInstallsToCurrentVersion()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.True(await TableExistsAsync(context, "ManufacturingEvents"));
    }

    [SqlServerFact]
    public async Task PreviousVersionWithDataUpgradesWithoutInventingManufacturingFacts()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MesMigrationIds.InitialFoundation);
        var materialId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO [mes].[Materials] ([Id], [Code], [Name], [TraceabilityMode], [IsActive])
            VALUES ({{materialId}}, N'OLD-ROUTER', N'可证明旧产品', N'None', 1);
            INSERT INTO [mes].[ProductionOrders]
                ([Id], [OrderNumber], [MaterialId], [PlannedQuantity], [Status], [CreatedAtUtc])
            VALUES
                ({{orderId}}, N'PO-LEGACY-001', {{materialId}}, 5, N'Created', '2026-07-01T00:00:00Z');
            """);

        await context.Database.MigrateAsync();

        var orderNumber = await context.ProductionOrders
            .Where(order => order.Id == orderId)
            .Select(order => order.OrderNumber)
            .SingleAsync();
        Assert.Equal("PO-LEGACY-001", orderNumber);
        Assert.False(await context.ManufacturingEvents.AnyAsync());
    }

    [SqlServerFact]
    public async Task CurrentSchemaEnforcesBusinessKeysAndPositiveQuantity()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var materialId = Guid.NewGuid();
        await InsertMaterialAsync(context, materialId, "MAT-UNIQUE");

        await Assert.ThrowsAsync<SqlException>(
            () => InsertMaterialAsync(context, Guid.NewGuid(), "MAT-UNIQUE"));

        var error = await Assert.ThrowsAsync<SqlException>(
            () => context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO [mes].[ProductionOrders]
                    ([Id], [OrderNumber], [MaterialId], [PlannedQuantity], [Status], [CreatedAtUtc], [SourceSystem])
                VALUES
                    ({{Guid.NewGuid()}}, N'PO-ZERO', {{materialId}}, 0, N'Created', SYSUTCDATETIME(), N'Manual');
                """));
        Assert.Equal(547, error.Number);
    }

    [SqlServerFact]
    public async Task FailedTransactionRollsBackAllDatabaseChanges()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();

        var error = await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await InsertMaterialAsync(context, Guid.NewGuid(), "MAT-ROLLBACK");
            await InsertMaterialAsync(context, Guid.NewGuid(), "MAT-ROLLBACK");
            await transaction.CommitAsync();
        });

        Assert.Equal(2601, error.Number);
        context.ChangeTracker.Clear();
        Assert.False(await context.Materials.AnyAsync(material => material.Code == "MAT-ROLLBACK"));
    }

    [SqlServerFact]
    public async Task ApplyingMigrationsRepeatedlyIsANoOp()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());

        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [SqlServerFact]
    public async Task CompatibilityCheckBlocksOutdatedSchemaAndAcceptsCurrentSchema()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.GetService<IMigrator>()
            .MigrateAsync(MesMigrationIds.InitialFoundation);
        var checker = new DatabaseCompatibilityChecker(context);

        var outdated = await checker.CheckAsync();
        await context.Database.MigrateAsync();
        var current = await checker.CheckAsync();

        Assert.False(outdated.IsReady);
        Assert.Equal("DB_SCHEMA_OUTDATED", outdated.Code);
        Assert.Contains(MesMigrationIds.EvolutionBaseline, outdated.PendingMigrations);
        Assert.True(current.IsReady);
        Assert.Equal("DB_READY", current.Code);
    }

    [SqlServerFact]
    public async Task ExplicitDemoInitializerIsIdempotent()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var initializer = new DemoDataInitializer(context);

        await initializer.LoadAsync();
        await initializer.LoadAsync();

        Assert.Equal(1, await context.UserAccounts.CountAsync());
        Assert.Equal(1, await context.Materials.CountAsync());
        Assert.Equal(1, await context.ProductionOrders.CountAsync());
        Assert.Equal("DemoInitializer", await context.ProductionOrders
            .Select(order => order.SourceSystem)
            .SingleAsync());
        Assert.False(await context.ManufacturingEvents.AnyAsync());
    }

    [SqlServerFact]
    public async Task ManufacturingEventsRejectUpdateAndDelete()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var eventId = Guid.NewGuid();
        context.ManufacturingEvents.Add(new ManufacturingEvent
        {
            Id = eventId,
            EventType = "BASELINE_TEST",
            AggregateType = "ProductionOrder",
            AggregateId = "PO-APPEND-ONLY",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Actor = "sql-server-gate",
            PayloadJson = "{}",
        });
        await context.SaveChangesAsync();

        var updateError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ManufacturingEvents]
                SET [Actor] = N'tampered'
                WHERE [Id] = {{eventId}};
                """));
        var deleteError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                DELETE FROM [mes].[ManufacturingEvents] WHERE [Id] = {{eventId}};
                """));

        Assert.Equal(51001, updateError.Number);
        Assert.Equal(51001, deleteError.Number);
        Assert.Equal("sql-server-gate", await context.ManufacturingEvents
            .Where(manufacturingEvent => manufacturingEvent.Id == eventId)
            .Select(manufacturingEvent => manufacturingEvent.Actor)
            .SingleAsync());
    }

    [SqlServerFact]
    public async Task RowVersionRejectsStaleConcurrentUpdate()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await using (var setup = CreateContext(connectionString))
        {
            await setup.Database.MigrateAsync();
            var initializer = new DemoDataInitializer(setup);
            await initializer.LoadAsync();
        }

        await using var firstContext = CreateContext(connectionString);
        await using var secondContext = CreateContext(connectionString);
        var firstOrder = await firstContext.ProductionOrders.SingleAsync();
        var staleOrder = await secondContext.ProductionOrders.SingleAsync();
        firstContext.Entry(firstOrder).Property(order => order.SourceReference).CurrentValue = "first-update";
        secondContext.Entry(staleOrder).Property(order => order.SourceReference).CurrentValue =
            "stale-update";

        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }

    private static async Task InsertMaterialAsync(
        MesDbContext context,
        Guid id,
        string code)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO [mes].[Materials] ([Id], [Code], [Name], [TraceabilityMode], [IsActive])
            VALUES ({{id}}, {{code}}, N'Test material', N'None', 1);
            """);
    }

    private static async Task<bool> TableExistsAsync(MesDbContext context, string tableName)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE [name] = @tableName";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        await context.Database.OpenConnectionAsync();
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture) == 1;
    }
}
