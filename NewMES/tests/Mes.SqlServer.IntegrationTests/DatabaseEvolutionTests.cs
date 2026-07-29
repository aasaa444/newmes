using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Seeding;
using Mes.Infrastructure.Security;
using Mes.Infrastructure.IdentityAccess;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Integration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Globalization;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class DatabaseEvolutionTests(SqlServerFixture server)
{
    private static readonly string[] CurrentMigrationIds =
    [
        MesMigrationIds.InitialFoundation,
        MesMigrationIds.EvolutionBaseline,
        MesMigrationIds.CapabilityRolesAuditContext,
        MesMigrationIds.IdempotentProductionOrderIngress,
        MesMigrationIds.PreserveErpIngressEvidence,
        MesMigrationIds.PreserveRejectedIngressGaps,
        MesMigrationIds.OrderReleaseSnapshotLifecycle,
        MesMigrationIds.LineSideMaterialTransactionLedger,
        MesMigrationIds.ControlledIdentityLabelStartWip,
        MesMigrationIds.AuthorizedIdentitySourcesAndReceipts,
        MesMigrationIds.AssemblyBindingConsumption,
    ];

    [SqlServerFact]
    public async Task EmptyDatabaseInstallsToCurrentVersion()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(CurrentMigrationIds, await context.Database.GetAppliedMigrationsAsync());
        Assert.True(await TableExistsAsync(context, "ManufacturingEvents"));
        Assert.True(await TableExistsAsync(context, "IntegrationInboxMessages"));
        Assert.True(await TableExistsAsync(context, "MaterialTransactions"));
        Assert.True(await TableExistsAsync(context, "ProductIdentities"));
        Assert.True(await TableExistsAsync(context, "ControlledIdentifiers"));
        Assert.True(await TableExistsAsync(context, "ProductLabels"));
        Assert.True(await TableExistsAsync(context, "IdentitySourceRegistrations"));
        Assert.True(await TableExistsAsync(context, "StartWipCommandReceipts"));
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
        Assert.Equal(
            ProductionOrderStatus.Received,
            await context.ProductionOrders
                .Where(order => order.Id == orderId)
                .Select(order => order.Status)
                .SingleAsync());
        Assert.Null(await context.ProductionOrders
            .Where(order => order.Id == orderId)
            .Select(order => order.SourceVersion)
            .SingleAsync());
        Assert.Null(await context.Materials
            .Where(material => material.Id == materialId)
            .Select(material => material.BaseUnit)
            .SingleAsync());
        Assert.False(await context.ManufacturingEvents.AnyAsync());
    }

    [SqlServerFact]
    public async Task Ticket06EventUpgradesWithConservativeRecordedTimeAndAppendOnlyProtection()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MesMigrationIds.LineSideMaterialTransactionLedger);
        var eventId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse(
            "2026-07-28T12:34:56+00:00",
            CultureInfo.InvariantCulture);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO [mes].[ManufacturingEvents]
                ([Id], [EventType], [AggregateType], [AggregateId], [OccurredAtUtc],
                 [Actor], [PayloadJson], [CorrectsEventId])
            VALUES
                ({{eventId}}, N'LEGACY_EVENT', N'ProductionOrder', N'PO-UPGRADE-07',
                 {{occurredAt}}, N'upgrade-test', N'{}', NULL);
            """);

        await context.Database.MigrateAsync();

        var upgraded = await context.ManufacturingEvents
            .AsNoTracking()
            .SingleAsync(item => item.Id == eventId);
        Assert.Equal(occurredAt, upgraded.RecordedAtUtc);
        var updateError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [mes].[ManufacturingEvents]
                SET [Actor] = N'tampered'
                WHERE [Id] = {{eventId}};
                """));
        Assert.Equal(51001, updateError.Number);
    }

    [SqlServerFact]
    public async Task LegacyIngressHashIsTaggedWithoutInventingMissingRawPayload()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MesMigrationIds.IdempotentProductionOrderIngress);
        var inboxId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO [integration].[IntegrationInboxMessages]
                ([Id], [SourceSystem], [MessageId], [BusinessKey], [SourceVersion],
                 [ContractVersion], [PayloadHash], [PayloadHashAlgorithm], [Status],
                 [ResultCode], [ResultMessage], [HttpStatusCode], [ReceivedAtUtc],
                 [ProcessedAtUtc], [ProductionOrderId])
            VALUES
                ({{inboxId}}, N'ERP-U8', N'MSG-LEGACY-HASH-04', N'PO-LEGACY-HASH-04', N'7',
                 N'1.0', REPLICATE('0', 64), N'SHA-256', N'Rejected',
                 N'MATERIAL_NOT_FOUND', N'旧版拒绝', 422, SYSUTCDATETIME(),
                 SYSUTCDATETIME(), NULL);
            """);

        await context.Database.MigrateAsync();

        var inbox = await context.IntegrationInboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == inboxId);
        Assert.Equal("SHA-256-DTO-V1", inbox.PayloadHashAlgorithm);
        Assert.Null(inbox.PayloadJson);
        Assert.Equal(IntegrationInboxStatus.Rejected, inbox.Status);
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
                    ({{Guid.NewGuid()}}, N'PO-ZERO', {{materialId}}, 0, N'Received', SYSUTCDATETIME(), N'Manual');
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
        Assert.Equal(CurrentMigrationIds, await context.Database.GetAppliedMigrationsAsync());
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
            RecordedAtUtc = DateTimeOffset.UtcNow,
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

    [SqlServerFact]
    public async Task ElevatedDatabaseConnectionIsRejectedForApiRuntime()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var probe = new SqlServerRuntimePrivilegeProbe(context);

        var result = await probe.CheckAsync();

        Assert.False(result.IsLeastPrivilege);
        Assert.Contains("SEC_DATABASE_HIGH_PRIVILEGE", result.ViolationCodes);
    }

    [SqlServerFact]
    public async Task LeastPrivilegeDatabaseUserIsAcceptedForApiRuntime()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        await context.Database.OpenConnectionAsync();
        var isImpersonating = false;
        try
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE USER [mes_runtime_probe] WITHOUT LOGIN;
                GRANT CONNECT TO [mes_runtime_probe];
                GRANT SELECT ON OBJECT::[dbo].[__EFMigrationsHistory] TO [mes_runtime_probe];
                EXECUTE AS USER = N'mes_runtime_probe';
                """);
            isImpersonating = true;
            var probe = new SqlServerRuntimePrivilegeProbe(context);

            var result = await probe.CheckAsync();

            Assert.True(result.IsLeastPrivilege);
            Assert.Empty(result.ViolationCodes);
        }
        finally
        {
            if (isImpersonating)
            {
                await context.Database.ExecuteSqlRawAsync("REVERT;");
            }

            await context.Database.ExecuteSqlRawAsync("DROP USER [mes_runtime_probe];");
        }
    }

    [SqlServerFact]
    public async Task DatabaseUserWithDdlPrivilegesIsRejectedForApiRuntime()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        await context.Database.OpenConnectionAsync();
        var isImpersonating = false;
        try
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE USER [mes_ddl_probe] WITHOUT LOGIN;
                ALTER ROLE [db_ddladmin] ADD MEMBER [mes_ddl_probe];
                EXECUTE AS USER = N'mes_ddl_probe';
                """);
            isImpersonating = true;
            var probe = new SqlServerRuntimePrivilegeProbe(context);

            var result = await probe.CheckAsync();

            Assert.False(result.IsLeastPrivilege);
            Assert.Contains("SEC_DATABASE_HIGH_PRIVILEGE", result.ViolationCodes);
        }
        finally
        {
            if (isImpersonating)
            {
                await context.Database.ExecuteSqlRawAsync("REVERT;");
            }

            await context.Database.ExecuteSqlRawAsync("""
                ALTER ROLE [db_ddladmin] DROP MEMBER [mes_ddl_probe];
                DROP USER [mes_ddl_probe];
                """);
        }
    }

    [SqlServerFact]
    public async Task BusinessAuditIsSeparateAndAppendOnly()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var auditId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO [audit].[BusinessAuditRecords]
                ([Id], [OccurredAtUtc], [ActorUserId], [ActorUsername], [ActorRolesSnapshot],
                 [AuthorizedRole],
                 [Capability], [Action], [BusinessObjectType], [BusinessObjectId],
                 [Result], [ReasonCode], [CorrelationId])
            VALUES
                ({{auditId}}, SYSDATETIMEOFFSET(), NULL, N'unknown.operator', N'Operator', NULL,
                 N'StationExecute', N'STATION_EXECUTION_REJECTED', N'ProductUnit',
                 N'SN-REJECTED-001', N'Denied', N'IDENTITY_NOT_ACTIVE', N'audit-test-001');
            """);

        var updateError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE [audit].[BusinessAuditRecords]
                SET [ReasonCode] = N'tampered'
                WHERE [Id] = {{auditId}};
                """));
        var deleteError = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($$"""
                DELETE FROM [audit].[BusinessAuditRecords] WHERE [Id] = {{auditId}};
                """));

        Assert.Equal(51002, updateError.Number);
        Assert.Equal(51002, deleteError.Number);
        Assert.Equal(0, await context.ManufacturingEvents.CountAsync());
    }

    [SqlServerFact]
    public async Task RoleChangesAffectSubsequentAuthorizationAndDeniedChangesAreAudited()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var adminId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        context.UserAccounts.AddRange(
            Account(adminId, "admin", BusinessRole.SystemAdministrator),
            Account(operatorId, "operator", BusinessRole.Operator));
        await context.SaveChangesAsync();
        var service = new IdentityAccessService(context, TimeProvider.System);
        var admin = await service.GetRequiredIdentityAsync(adminId);

        await service.ChangeRolesAsync(
            admin,
            operatorId,
            BusinessRole.Operator,
            [BusinessRole.Operator, BusinessRole.QualityEngineer],
            "role-change-001");
        context.ChangeTracker.Clear();
        var changedOperator = await service.GetRequiredIdentityAsync(operatorId);

        Assert.Contains(
            BusinessCapability.QualityDispositionApprove,
            changedOperator.Capabilities);
        Assert.Equal(BusinessRole.Operator, changedOperator.PrimaryRole);

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => service.ChangeRolesAsync(
            changedOperator,
            adminId,
            BusinessRole.SystemAdministrator,
            [BusinessRole.SystemAdministrator],
            "role-change-denied-001"));
        var audit = await service.ReadAuditAsync(admin, 20, "audit-read-001");

        Assert.Contains(audit, record =>
            record.CorrelationId == "role-change-denied-001"
            && record.Result == Mes.Domain.Auditing.BusinessAuditResult.Denied
            && record.ActorRolesSnapshot == "Operator,QualityEngineer"
            && record.Capability == BusinessCapability.AccountManage);
        Assert.Contains(audit, record =>
            record.CorrelationId == "audit-read-001"
            && record.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded
            && record.AuthorizedRole == BusinessRole.SystemAdministrator
            && record.Capability == BusinessCapability.BusinessAuditRead);
    }

    [SqlServerFact]
    public async Task InitialAdministratorBootstrapIsControlledAuditedAndOneTime()
    {
        await using var context = CreateContext(await server.CreateDatabaseAsync());
        await context.Database.MigrateAsync();
        var bootstrapper = new InitialAdministratorBootstrapper(
            context,
            new PasswordHasher<UserAccount>(),
            TimeProvider.System);

        await bootstrapper.BootstrapAsync(
            "mes.admin",
            "MES Administrator",
            "IntegrationOnly-InitialAdmin-03!",
            "bootstrap-001");
        context.ChangeTracker.Clear();

        var account = await context.UserAccounts
            .Include(user => user.RoleAssignments)
            .SingleAsync(user => user.Username == "mes.admin");
        Assert.True(account.IsActive);
        Assert.Equal(BusinessRole.SystemAdministrator, account.PrimaryRole);
        Assert.Contains(
            account.RoleAssignments,
            assignment => assignment.Role == BusinessRole.SystemAdministrator);
        Assert.NotNull(account.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<UserAccount>().VerifyHashedPassword(
                account,
                account.PasswordHash,
                "IntegrationOnly-InitialAdmin-03!"));
        Assert.Contains(
            await context.BusinessAuditRecords.ToArrayAsync(),
            record => record.CorrelationId == "bootstrap-001"
                && record.Action == "INITIAL_ADMIN_BOOTSTRAP"
                && record.Result == Mes.Domain.Auditing.BusinessAuditResult.Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.BootstrapAsync(
            "second.admin",
            "Second Administrator",
            "IntegrationOnly-SecondAdmin-03!",
            "bootstrap-002"));
        Assert.Equal(
            1,
            await context.UserRoleAssignments.CountAsync(
                assignment => assignment.Role == BusinessRole.SystemAdministrator));
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }

    private static UserAccount Account(Guid id, string username, BusinessRole role)
    {
        var account = new UserAccount
        {
            Id = id,
            Username = username,
            DisplayName = username,
            IsActive = true,
            PrimaryRole = role,
        };
        account.RoleAssignments.Add(new UserRoleAssignment
        {
            UserAccountId = id,
            Role = role,
        });
        return account;
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
