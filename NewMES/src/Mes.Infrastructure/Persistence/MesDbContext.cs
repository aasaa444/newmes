using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Integration;
using Mes.Domain.Materials;
using Mes.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Persistence;

public sealed class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    public DbSet<BusinessAuditRecord> BusinessAuditRecords => Set<BusinessAuditRecord>();

    public DbSet<IntegrationInboxMessage> IntegrationInboxMessages =>
        Set<IntegrationInboxMessage>();

    public DbSet<IntegrationInboxConflict> IntegrationInboxConflicts =>
        Set<IntegrationInboxConflict>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();

    public DbSet<ProductExecutionTemplateVersion> ProductExecutionTemplateVersions =>
        Set<ProductExecutionTemplateVersion>();

    public DbSet<ProductionOrderExecutionSnapshot> ProductionOrderExecutionSnapshots =>
        Set<ProductionOrderExecutionSnapshot>();

    public DbSet<ManufacturingEvent> ManufacturingEvents => Set<ManufacturingEvent>();

    public DbSet<ProductIdentity> ProductIdentities => Set<ProductIdentity>();

    public DbSet<ControlledIdentifier> ControlledIdentifiers => Set<ControlledIdentifier>();

    public DbSet<ProductLabel> ProductLabels => Set<ProductLabel>();

    public DbSet<StartWipCommandReceipt> StartWipCommandReceipts => Set<StartWipCommandReceipt>();

    public DbSet<AssemblyComponentBinding> AssemblyComponentBindings =>
        Set<AssemblyComponentBinding>();

    public DbSet<AssemblyCommandReceipt> AssemblyCommandReceipts => Set<AssemblyCommandReceipt>();

    public DbSet<FirmwareConfigurationExecution> FirmwareConfigurationExecutions =>
        Set<FirmwareConfigurationExecution>();

    public DbSet<TestSpecificationVersion> TestSpecificationVersions =>
        Set<TestSpecificationVersion>();

    public DbSet<TestRun> TestRuns => Set<TestRun>();

    public DbSet<TestMeasurement> TestMeasurements => Set<TestMeasurement>();

    public DbSet<IdentitySourceRegistration> IdentitySourceRegistrations =>
        Set<IdentitySourceRegistration>();

    public DbSet<IdentitySourceIdentifierGrant> IdentitySourceIdentifierGrants =>
        Set<IdentitySourceIdentifierGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable(
                "UserAccounts",
                "security",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_UserAccounts_PrimaryRole",
                        $"[PrimaryRole] IS NULL OR {RoleCheckSql("PrimaryRole")}");
                    table.HasCheckConstraint(
                        "CK_UserAccounts_PasswordHash",
                        "[PasswordHash] IS NULL OR LEN([PasswordHash]) > 0");
                });
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Username).HasMaxLength(80);
            entity.Property(account => account.DisplayName).HasMaxLength(120);
            entity.Property(account => account.PasswordHash).HasMaxLength(512);
            entity.Property(account => account.PrimaryRole)
                .HasConversion<string>()
                .HasMaxLength(40);
            entity.HasIndex(account => account.Username).IsUnique();
        });

        modelBuilder.Entity<UserRoleAssignment>(entity =>
        {
            entity.ToTable(
                "UserAccountRoles",
                "security",
                table => table.HasCheckConstraint(
                    "CK_UserAccountRoles_Role",
                    RoleCheckSql("Role")));
            entity.HasKey(assignment => new { assignment.UserAccountId, assignment.Role });
            entity.Property(assignment => assignment.Role)
                .HasConversion<string>()
                .HasMaxLength(40);
            entity.HasOne(assignment => assignment.UserAccount)
                .WithMany(account => account.RoleAssignments)
                .HasForeignKey(assignment => assignment.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessAuditRecord>(entity =>
        {
            entity.ToTable(
                "BusinessAuditRecords",
                "audit",
                table =>
                {
                    table.HasTrigger("TR_BusinessAuditRecords_AppendOnly");
                    table.HasCheckConstraint(
                        "CK_BusinessAuditRecords_Result",
                        "[Result] IN ('Succeeded', 'Denied')");
                    table.HasCheckConstraint(
                        "CK_BusinessAuditRecords_AuthorizedRole",
                        $"[AuthorizedRole] IS NULL OR {RoleCheckSql("AuthorizedRole")}");
                });
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.ActorUsername).HasMaxLength(80);
            entity.Property(audit => audit.ActorRolesSnapshot).HasMaxLength(400);
            entity.Property(audit => audit.AuthorizedRole)
                .HasConversion<string>()
                .HasMaxLength(40);
            entity.Property(audit => audit.Capability)
                .HasConversion<string>()
                .HasMaxLength(80);
            entity.Property(audit => audit.Action).HasMaxLength(80);
            entity.Property(audit => audit.BusinessObjectType).HasMaxLength(80);
            entity.Property(audit => audit.BusinessObjectId).HasMaxLength(160);
            entity.Property(audit => audit.Result)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(audit => audit.ReasonCode).HasMaxLength(80);
            entity.Property(audit => audit.CorrelationId).HasMaxLength(64);
            entity.HasIndex(audit => audit.OccurredAtUtc);
            entity.HasIndex(audit => new
            {
                audit.BusinessObjectType,
                audit.BusinessObjectId,
                audit.OccurredAtUtc,
            });
            entity.HasOne(audit => audit.ActorUser)
                .WithMany()
                .HasForeignKey(audit => audit.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Material>(entity =>
        {
            entity.ToTable(
                "Materials",
                "mes",
                table => table.HasCheckConstraint(
                    "CK_Materials_TraceabilityMode",
                    "[TraceabilityMode] IN ('None', 'Lot', 'Serial')"));
            entity.HasKey(material => material.Id);
            entity.Property(material => material.Code).HasMaxLength(80);
            entity.Property(material => material.Name).HasMaxLength(200);
            entity.Property(material => material.BaseUnit)
                .HasMaxLength(24)
                .IsRequired(false);
            entity.Property(material => material.TraceabilityMode)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.HasIndex(material => material.Code).IsUnique();
        });

        modelBuilder.Entity<MaterialTransaction>(entity =>
        {
            entity.ToTable(
                "MaterialTransactions",
                "mes",
                table =>
                {
                    table.HasTrigger("TR_MaterialTransactions_AppendOnly");
                    table.HasCheckConstraint(
                        "CK_MaterialTransactions_Type",
                        "[TransactionType] IN ('LineSideTransfer', 'OrderIssue', 'OrderReturn', 'Consumption', 'Reversal', 'Adjustment')");
                    table.HasCheckConstraint(
                        "CK_MaterialTransactions_Quantity",
                        "[Quantity] > 0");
                    table.HasCheckConstraint(
                        "CK_MaterialTransactions_DeltaShape",
                        "([TransactionType] = 'LineSideTransfer' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0) OR "
                        + "([TransactionType] = 'OrderIssue' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = -[Quantity] AND [OrderAvailableQuantityDelta] = [Quantity] AND [OrderIssuedQuantityDelta] = [Quantity]) OR "
                        + "([TransactionType] = 'OrderReturn' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = -[Quantity]) OR "
                        + "([TransactionType] = 'Consumption' AND [ProductionOrderId] IS NOT NULL AND [ProductIdentityId] IS NOT NULL AND [OperationCode] IS NOT NULL AND [TraceabilityMode] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = 0 AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = 0) OR "
                        + "([TransactionType] = 'Reversal' AND [ReversesTransactionId] IS NOT NULL) OR "
                        + "([TransactionType] = 'Adjustment' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND ABS([LineSideQuantityDelta]) = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0)");
                    table.HasCheckConstraint(
                        "CK_MaterialTransactions_Reason",
                        "[TransactionType] NOT IN ('OrderReturn', 'Reversal', 'Adjustment') OR LEN([Reason]) > 0");
                });
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.TransactionType)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(transaction => transaction.LotNumber).HasMaxLength(120);
            entity.Property(transaction => transaction.Quantity).HasPrecision(18, 6);
            entity.Property(transaction => transaction.Unit).HasMaxLength(24);
            entity.Property(transaction => transaction.LineSideQuantityDelta).HasPrecision(18, 6);
            entity.Property(transaction => transaction.OrderAvailableQuantityDelta).HasPrecision(18, 6);
            entity.Property(transaction => transaction.OrderIssuedQuantityDelta).HasPrecision(18, 6);
            entity.Property(transaction => transaction.OperationCode).HasMaxLength(80);
            entity.Property(transaction => transaction.TraceabilityMode)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(transaction => transaction.SourceSystem).HasMaxLength(80);
            entity.Property(transaction => transaction.IdempotencyKey).HasMaxLength(120);
            entity.Property(transaction => transaction.SourceDocumentType).HasMaxLength(80);
            entity.Property(transaction => transaction.SourceDocumentNumber).HasMaxLength(160);
            entity.Property(transaction => transaction.FromParty).HasMaxLength(160);
            entity.Property(transaction => transaction.ToParty).HasMaxLength(160);
            entity.Property(transaction => transaction.Reason).HasMaxLength(400);
            entity.Property(transaction => transaction.CommandHash).HasMaxLength(64);
            entity.Property(transaction => transaction.CommandHashAlgorithm).HasMaxLength(24);
            entity.Property(transaction => transaction.CorrelationId).HasMaxLength(64);
            entity.Property(transaction => transaction.LineSideBalanceAfter).HasPrecision(18, 6);
            entity.Property(transaction => transaction.OrderAvailableBalanceAfter).HasPrecision(18, 6);
            entity.HasIndex(transaction => new
            {
                transaction.SourceSystem,
                transaction.IdempotencyKey,
            }).IsUnique();
            entity.HasIndex(transaction => transaction.ReversesTransactionId)
                .IsUnique()
                .HasFilter("[ReversesTransactionId] IS NOT NULL");
            entity.HasIndex(transaction => new
            {
                transaction.MaterialId,
                transaction.LotNumber,
                transaction.RecordedAtUtc,
            });
            entity.HasIndex(transaction => new
            {
                transaction.ProductionOrderId,
                transaction.MaterialId,
                transaction.LotNumber,
                transaction.RecordedAtUtc,
            });
            entity.HasIndex(transaction => new
            {
                transaction.ProductIdentityId,
                transaction.MaterialId,
                transaction.OperationCode,
                transaction.RecordedAtUtc,
            });
            entity.HasOne(transaction => transaction.Material)
                .WithMany()
                .HasForeignKey(transaction => transaction.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.ProductionOrder)
                .WithMany()
                .HasForeignKey(transaction => transaction.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.ProductIdentity)
                .WithMany()
                .HasForeignKey(transaction => transaction.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.ReversesTransaction)
                .WithMany()
                .HasForeignKey(transaction => transaction.ReversesTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.ActorUser)
                .WithMany()
                .HasForeignKey(transaction => transaction.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductionOrder>(entity =>
        {
            entity.ToTable(
                "ProductionOrders",
                "mes",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_ProductionOrders_PlannedQuantity",
                        "[PlannedQuantity] > 0");
                    table.HasCheckConstraint(
                        "CK_ProductionOrders_Status",
                        "[Status] IN ('Received', 'Released', 'InProduction', 'Paused', 'ExecutionCompleted', 'Closed', 'Cancelled')");
                    table.HasCheckConstraint(
                        "CK_ProductionOrders_QuantityBalance",
                        "[StartedQuantity] >= 0 AND [QualifiedQuantity] >= 0 AND [ScrappedQuantity] >= 0 AND [OpenQualityHoldQuantity] >= 0 AND [StartedQuantity] <= [PlannedQuantity] AND [QualifiedQuantity] + [ScrappedQuantity] <= [StartedQuantity] AND [OpenQualityHoldQuantity] <= [StartedQuantity]");
                });
            entity.HasKey(order => order.Id);
            entity.Property(order => order.OrderNumber).HasMaxLength(80);
            entity.Property(order => order.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
            entity.Property(order => order.SourceSystem).HasMaxLength(80);
            entity.Property(order => order.SourceReference).HasMaxLength(160);
            entity.Property(order => order.SourceVersion).HasMaxLength(80);
            entity.Property(order => order.Version).IsRowVersion();
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasIndex(order => new { order.SourceSystem, order.SourceReference })
                .IsUnique()
                .HasFilter("[SourceSystem] IS NOT NULL AND [SourceReference] IS NOT NULL");
            entity.HasOne(order => order.Material)
                .WithMany()
                .HasForeignKey(order => order.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductExecutionTemplateVersion>(entity =>
        {
            entity.ToTable(
                "ProductExecutionTemplateVersions",
                "mes",
                table => table.HasTrigger("TR_ProductExecutionTemplateVersions_AppendOnly"));
            entity.HasKey(template => template.Id);
            entity.Property(template => template.Version).HasMaxLength(80);
            entity.Property(template => template.Applicability).HasMaxLength(400);
            entity.Property(template => template.DefinitionJson).HasColumnType("nvarchar(max)");
            entity.Property(template => template.DefinitionHash).HasMaxLength(64);
            entity.Property(template => template.DefinitionHashAlgorithm).HasMaxLength(24);
            entity.HasIndex(template => new { template.MaterialId, template.Version }).IsUnique();
            entity.HasIndex(template => new { template.MaterialId, template.PublishedAtUtc });
            entity.HasOne(template => template.Material)
                .WithMany()
                .HasForeignKey(template => template.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(template => template.PublishedByUser)
                .WithMany()
                .HasForeignKey(template => template.PublishedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductionOrderExecutionSnapshot>(entity =>
        {
            entity.ToTable(
                "ProductionOrderExecutionSnapshots",
                "mes",
                table => table.HasTrigger("TR_ProductionOrderExecutionSnapshots_AppendOnly"));
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.SnapshotVersion).HasMaxLength(80);
            entity.Property(snapshot => snapshot.DefinitionJson).HasColumnType("nvarchar(max)");
            entity.Property(snapshot => snapshot.DefinitionHash).HasMaxLength(64);
            entity.Property(snapshot => snapshot.DefinitionHashAlgorithm).HasMaxLength(24);
            entity.HasIndex(snapshot => snapshot.ProductionOrderId).IsUnique();
            entity.HasOne(snapshot => snapshot.ProductionOrder)
                .WithOne()
                .HasForeignKey<ProductionOrderExecutionSnapshot>(snapshot => snapshot.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(snapshot => snapshot.SourceTemplate)
                .WithMany()
                .HasForeignKey(snapshot => snapshot.SourceTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(snapshot => snapshot.CreatedByUser)
                .WithMany()
                .HasForeignKey(snapshot => snapshot.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductIdentity>(entity =>
        {
            entity.ToTable(
                "ProductIdentities",
                "mes",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_ProductIdentities_Status",
                        "[Status] IN ('Allocated', 'Bound', 'Voided')");
                    table.HasCheckConstraint(
                        "CK_ProductIdentities_BindingShape",
                        "([Status] = 'Bound' AND [ProductionOrderId] IS NOT NULL AND [ExecutionSnapshotId] IS NOT NULL AND [BoundAtUtc] IS NOT NULL AND [StartSourceSystem] IS NOT NULL AND [StartIdempotencyKey] IS NOT NULL AND [StartCommandHash] IS NOT NULL) OR ([Status] IN ('Allocated', 'Voided') AND [ProductionOrderId] IS NULL AND [ExecutionSnapshotId] IS NULL AND [BoundAtUtc] IS NULL AND [StartSourceSystem] IS NULL AND [StartIdempotencyKey] IS NULL AND [StartCommandHash] IS NULL)");
                });
            entity.HasKey(identity => identity.Id);
            entity.Property(identity => identity.SerialNumber).HasMaxLength(200);
            entity.Property(identity => identity.SerialSourceType)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(identity => identity.SerialSourceSystem).HasMaxLength(80);
            entity.Property(identity => identity.SerialSourceReference).HasMaxLength(200);
            entity.Property(identity => identity.Status)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(identity => identity.NextOperationCode).HasMaxLength(80);
            entity.Property(identity => identity.StartSourceSystem).HasMaxLength(80);
            entity.Property(identity => identity.StartIdempotencyKey).HasMaxLength(120);
            entity.Property(identity => identity.StartCommandHash).HasMaxLength(64);
            entity.Property(identity => identity.AllocationSourceSystem).HasMaxLength(80);
            entity.Property(identity => identity.AllocationIdempotencyKey).HasMaxLength(120);
            entity.Property(identity => identity.AllocationCommandHash).HasMaxLength(64);
            entity.Property(identity => identity.Version).IsRowVersion();
            entity.HasIndex(identity => identity.SerialNumber).IsUnique();
            entity.HasIndex(identity => new
            {
                identity.AllocationSourceSystem,
                identity.AllocationIdempotencyKey,
            }).IsUnique();
            entity.HasIndex(identity => new
            {
                identity.StartSourceSystem,
                identity.StartIdempotencyKey,
            }).IsUnique().HasFilter(
                "[StartSourceSystem] IS NOT NULL AND [StartIdempotencyKey] IS NOT NULL");
            entity.HasOne(identity => identity.Material)
                .WithMany()
                .HasForeignKey(identity => identity.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(identity => identity.ProductionOrder)
                .WithMany()
                .HasForeignKey(identity => identity.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(identity => identity.ExecutionSnapshot)
                .WithMany()
                .HasForeignKey(identity => identity.ExecutionSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ControlledIdentifier>(entity =>
        {
            entity.ToTable(
                "ControlledIdentifiers",
                "mes",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_ControlledIdentifiers_Type",
                        "[Type] IN ('SerialNumber', 'MacAddress', 'Imei', 'Certificate')");
                    table.HasCheckConstraint(
                        "CK_ControlledIdentifiers_SourceType",
                        "[SourceType] IN ('Erp', 'LabelSystem', 'MesControlledPool', 'DemoControlledPool')");
                    table.HasCheckConstraint(
                        "CK_ControlledIdentifiers_DemoFlag",
                        "([SourceType] = 'DemoControlledPool' AND [IsDemo] = 1) OR ([SourceType] <> 'DemoControlledPool' AND [IsDemo] = 0)");
                });
            entity.HasKey(identifier => identifier.Id);
            entity.Property(identifier => identifier.Type)
                .HasConversion<string>()
                .HasMaxLength(24);
            entity.Property(identifier => identifier.Value).HasMaxLength(200);
            entity.Property(identifier => identifier.SourceType)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(identifier => identifier.SourceSystem).HasMaxLength(80);
            entity.Property(identifier => identifier.SourceReference).HasMaxLength(200);
            entity.HasIndex(identifier => new { identifier.Type, identifier.Value }).IsUnique();
            entity.HasIndex(identifier => new { identifier.ProductIdentityId, identifier.Type }).IsUnique();
            entity.HasOne(identifier => identifier.ProductIdentity)
                .WithMany()
                .HasForeignKey(identifier => identifier.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssemblyComponentBinding>(entity =>
        {
            entity.ToTable(
                "AssemblyComponentBindings",
                "mes",
                table => table.HasCheckConstraint(
                    "CK_AssemblyComponentBindings_State",
                    "([IsActive] = 1 AND [UnboundAtUtc] IS NULL AND [UnboundByUserId] IS NULL AND [CorrectionReason] IS NULL) OR ([IsActive] = 0 AND [UnboundAtUtc] IS NOT NULL AND [UnboundByUserId] IS NOT NULL AND LEN([CorrectionReason]) > 0)"));
            entity.HasKey(binding => binding.Id);
            entity.Property(binding => binding.ComponentSerialNumber).HasMaxLength(200);
            entity.Property(binding => binding.LotNumber).HasMaxLength(120);
            entity.Property(binding => binding.Quantity).HasPrecision(18, 6);
            entity.Property(binding => binding.Unit).HasMaxLength(24);
            entity.Property(binding => binding.OperationCode).HasMaxLength(80);
            entity.Property(binding => binding.CorrectionReason).HasMaxLength(400);
            entity.Property(binding => binding.Version).IsRowVersion();
            entity.HasIndex(binding => binding.ComponentSerialNumber)
                .IsUnique()
                .HasFilter("[IsActive] = 1");
            entity.HasIndex(binding => new
            {
                binding.ProductIdentityId,
                binding.MaterialId,
                binding.OperationCode,
                binding.IsActive,
            });
            entity.HasIndex(binding => binding.ConsumptionTransactionId).IsUnique();
            entity.HasIndex(binding => binding.BindingEventId).IsUnique();
            entity.HasOne(binding => binding.ProductIdentity)
                .WithMany()
                .HasForeignKey(binding => binding.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrder>()
                .WithMany()
                .HasForeignKey(binding => binding.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrderExecutionSnapshot>()
                .WithMany()
                .HasForeignKey(binding => binding.ExecutionSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(binding => binding.Material)
                .WithMany()
                .HasForeignKey(binding => binding.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(binding => binding.ConsumptionTransaction)
                .WithMany()
                .HasForeignKey(binding => binding.ConsumptionTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(binding => binding.BindingEvent)
                .WithMany()
                .HasForeignKey(binding => binding.BindingEventId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(binding => binding.BoundByUser)
                .WithMany()
                .HasForeignKey(binding => binding.BoundByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(binding => binding.UnboundByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssemblyCommandReceipt>(entity =>
        {
            entity.ToTable(
                "AssemblyCommandReceipts",
                "mes",
                table => table.HasTrigger("TR_AssemblyCommandReceipts_AppendOnly"));
            entity.HasKey(receipt => receipt.Id);
            entity.Property(receipt => receipt.SourceSystem).HasMaxLength(80);
            entity.Property(receipt => receipt.IdempotencyKey).HasMaxLength(120);
            entity.Property(receipt => receipt.CommandType).HasMaxLength(40);
            entity.Property(receipt => receipt.CommandHash).HasMaxLength(64);
            entity.Property(receipt => receipt.CommandHashAlgorithm).HasMaxLength(24);
            entity.Property(receipt => receipt.ResultJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(receipt => new
            {
                receipt.SourceSystem,
                receipt.IdempotencyKey,
            }).IsUnique();
            entity.HasIndex(receipt => new
            {
                receipt.ProductIdentityId,
                receipt.CompletedAtUtc,
            });
            entity.HasOne<ProductIdentity>()
                .WithMany()
                .HasForeignKey(receipt => receipt.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FirmwareConfigurationExecution>(entity =>
        {
            entity.ToTable(
                "FirmwareConfigurationExecutions",
                "mes",
                table =>
                {
                    table.HasTrigger("TR_FirmwareConfigurationExecutions_AppendOnly");
                    table.HasCheckConstraint(
                        "CK_FirmwareConfigurationExecutions_Result",
                        "[Result] IN ('Succeeded', 'Failed')");
                    table.HasCheckConstraint(
                        "CK_FirmwareConfigurationExecutions_Time",
                        "[EndedAtUtc] >= [StartedAtUtc]");
                });
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.RequirementCode).HasMaxLength(80);
            entity.Property(execution => execution.OperationCode).HasMaxLength(80);
            entity.Property(execution => execution.RequiredVersion).HasMaxLength(120);
            entity.Property(execution => execution.ActualVersion).HasMaxLength(120);
            entity.Property(execution => execution.RequiredConfigurationPackage).HasMaxLength(160);
            entity.Property(execution => execution.ActualConfigurationPackage).HasMaxLength(160);
            entity.Property(execution => execution.RequiredChecksumAlgorithm).HasMaxLength(40);
            entity.Property(execution => execution.ActualChecksumAlgorithm).HasMaxLength(40);
            entity.Property(execution => execution.ExpectedChecksum).HasMaxLength(256);
            entity.Property(execution => execution.ActualChecksum).HasMaxLength(256);
            entity.Property(execution => execution.ToolId).HasMaxLength(120);
            entity.Property(execution => execution.ToolVersion).HasMaxLength(80);
            entity.Property(execution => execution.Result).HasConversion<string>().HasMaxLength(16);
            entity.Property(execution => execution.NextOperationCodeAfter).HasMaxLength(80);
            entity.Property(execution => execution.DiagnosticCode).HasMaxLength(80);
            entity.Property(execution => execution.DiagnosticMessage).HasMaxLength(1000);
            entity.Property(execution => execution.ReportedDiagnosticCode).HasMaxLength(80);
            entity.Property(execution => execution.ReportedDiagnosticMessage).HasMaxLength(1000);
            entity.Property(execution => execution.SourceSystem).HasMaxLength(80);
            entity.Property(execution => execution.IdempotencyKey).HasMaxLength(120);
            entity.Property(execution => execution.CommandHash).HasMaxLength(64);
            entity.Property(execution => execution.CommandHashAlgorithm).HasMaxLength(24);
            entity.Property(execution => execution.ActorUsername).HasMaxLength(120);
            entity.Property(execution => execution.Location).HasMaxLength(120);
            entity.Property(execution => execution.CorrelationId).HasMaxLength(64);
            entity.HasIndex(execution => new { execution.SourceSystem, execution.IdempotencyKey })
                .IsUnique();
            entity.HasIndex(execution => new
            {
                execution.ProductIdentityId,
                execution.RequirementCode,
            }).IsUnique().HasFilter("[Result] = 'Succeeded'");
            entity.HasIndex(execution => new { execution.ProductIdentityId, execution.RecordedAtUtc });
            entity.HasIndex(execution => execution.ManufacturingEventId).IsUnique();
            entity.HasOne(execution => execution.ProductIdentity)
                .WithMany()
                .HasForeignKey(execution => execution.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrder>()
                .WithMany()
                .HasForeignKey(execution => execution.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrderExecutionSnapshot>()
                .WithMany()
                .HasForeignKey(execution => execution.ExecutionSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(execution => execution.RetryOfExecution)
                .WithMany()
                .HasForeignKey(execution => execution.RetryOfExecutionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(execution => execution.ManufacturingEvent)
                .WithMany()
                .HasForeignKey(execution => execution.ManufacturingEventId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(execution => execution.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TestSpecificationVersion>(entity =>
        {
            entity.ToTable(
                "TestSpecificationVersions",
                "mes",
                table =>
                {
                    table.HasTrigger("TR_TestSpecificationVersions_ControlledApproval");
                    table.HasCheckConstraint(
                        "CK_TestSpecificationVersions_Approval",
                        "([IsApproved] = 0 AND [ApprovedAtUtc] IS NULL "
                        + "AND [ApprovedByUserId] IS NULL AND [ApprovedByUsername] IS NULL "
                        + "AND [ApprovalEvidenceReference] IS NULL) OR "
                        + "([IsApproved] = 1 AND [ApprovedAtUtc] IS NOT NULL "
                        + "AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUsername] IS NOT NULL "
                        + "AND [ApprovalEvidenceReference] IS NOT NULL)");
                });
            entity.HasKey(specification => specification.Id);
            entity.Property(specification => specification.Code).HasMaxLength(80);
            entity.Property(specification => specification.Version).HasMaxLength(80);
            entity.Property(specification => specification.OperationCode).HasMaxLength(80);
            entity.Property(specification => specification.Applicability).HasMaxLength(400);
            entity.Property(specification => specification.DefinitionJson).HasColumnType("nvarchar(max)");
            entity.Property(specification => specification.DefinitionHash).HasMaxLength(64);
            entity.Property(specification => specification.DefinitionHashAlgorithm).HasMaxLength(24);
            entity.Property(specification => specification.ApprovedByUsername).HasMaxLength(120);
            entity.Property(specification => specification.ApprovalEvidenceReference).HasMaxLength(400);
            entity.HasIndex(specification => new { specification.Code, specification.Version }).IsUnique();
            entity.HasIndex(specification => new
            {
                specification.MaterialId,
                specification.OperationCode,
                specification.IsApproved,
            });
            entity.HasOne<Material>()
                .WithMany()
                .HasForeignKey(specification => specification.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(specification => specification.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(specification => specification.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TestRun>(entity =>
        {
            entity.ToTable(
                "TestRuns",
                "mes",
                table =>
                {
                    table.HasTrigger("TR_TestRuns_AppendOnly");
                    table.HasCheckConstraint(
                        "CK_TestRuns_Result",
                        "[Result] IN ('Succeeded', 'Failed')");
                    table.HasCheckConstraint(
                        "CK_TestRuns_Time",
                        "[EndedAtUtc] >= [StartedAtUtc]");
                });
            entity.HasKey(run => run.Id);
            entity.Property(run => run.SpecificationCode).HasMaxLength(80);
            entity.Property(run => run.SpecificationVersion).HasMaxLength(80);
            entity.Property(run => run.SpecificationDefinitionHash).HasMaxLength(64);
            entity.Property(run => run.OperationCode).HasMaxLength(80);
            entity.Property(run => run.DeviceId).HasMaxLength(120);
            entity.Property(run => run.DeviceVersion).HasMaxLength(80);
            entity.Property(run => run.FixtureId).HasMaxLength(120);
            entity.Property(run => run.FixtureVersion).HasMaxLength(80);
            entity.Property(run => run.RawReportReference).HasMaxLength(400);
            entity.Property(run => run.Result).HasConversion<string>().HasMaxLength(16);
            entity.Property(run => run.DiagnosticCode).HasMaxLength(80);
            entity.Property(run => run.DiagnosticMessage).HasMaxLength(1000);
            entity.Property(run => run.SourceSystem).HasMaxLength(80);
            entity.Property(run => run.IdempotencyKey).HasMaxLength(120);
            entity.Property(run => run.CommandHash).HasMaxLength(64);
            entity.Property(run => run.CommandHashAlgorithm).HasMaxLength(24);
            entity.Property(run => run.ActorUsername).HasMaxLength(120);
            entity.Property(run => run.Location).HasMaxLength(120);
            entity.Property(run => run.CorrelationId).HasMaxLength(64);
            entity.Property(run => run.NextOperationCodeAfter).HasMaxLength(80);
            entity.HasIndex(run => new { run.SourceSystem, run.IdempotencyKey }).IsUnique();
            entity.HasIndex(run => new
            {
                run.ProductIdentityId,
                run.SpecificationCode,
                run.SpecificationVersion,
            }).IsUnique().HasFilter("[Result] = 'Succeeded'");
            entity.HasIndex(run => new { run.ProductIdentityId, run.RecordedAtUtc });
            entity.HasIndex(run => run.ManufacturingEventId).IsUnique();
            entity.HasOne(run => run.ProductIdentity)
                .WithMany()
                .HasForeignKey(run => run.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrder>()
                .WithMany()
                .HasForeignKey(run => run.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrderExecutionSnapshot>()
                .WithMany()
                .HasForeignKey(run => run.ExecutionSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(run => run.RetryOfTestRun)
                .WithMany()
                .HasForeignKey(run => run.RetryOfTestRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(run => run.ManufacturingEvent)
                .WithMany()
                .HasForeignKey(run => run.ManufacturingEventId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(run => run.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TestMeasurement>(entity =>
        {
            entity.ToTable(
                "TestMeasurements",
                "mes",
                table =>
                {
                    table.HasTrigger("TR_TestMeasurements_AppendOnly");
                    table.HasCheckConstraint(
                        "CK_TestMeasurements_Result",
                        "[Result] IN ('Passed', 'Failed')");
                });
            entity.HasKey(measurement => measurement.Id);
            entity.Property(measurement => measurement.ItemCode).HasMaxLength(80);
            entity.Property(measurement => measurement.ItemName).HasMaxLength(200);
            entity.Property(measurement => measurement.DataType).HasMaxLength(20);
            entity.Property(measurement => measurement.RawValue).HasMaxLength(1000);
            entity.Property(measurement => measurement.Unit).HasMaxLength(40);
            entity.Property(measurement => measurement.LowerLimit).HasPrecision(18, 6);
            entity.Property(measurement => measurement.UpperLimit).HasPrecision(18, 6);
            entity.Property(measurement => measurement.NumericValue).HasPrecision(18, 6);
            entity.Property(measurement => measurement.ExpectedText).HasMaxLength(1000);
            entity.Property(measurement => measurement.Result).HasConversion<string>().HasMaxLength(16);
            entity.Property(measurement => measurement.DiagnosticCode).HasMaxLength(80);
            entity.Property(measurement => measurement.DiagnosticMessage).HasMaxLength(1000);
            entity.Property(measurement => measurement.ReportedDiagnosticCode).HasMaxLength(80);
            entity.Property(measurement => measurement.ReportedDiagnosticMessage).HasMaxLength(1000);
            entity.HasIndex(measurement => new { measurement.TestRunId, measurement.ItemCode })
                .IsUnique();
            entity.HasOne(measurement => measurement.TestRun)
                .WithMany(run => run.Measurements)
                .HasForeignKey(measurement => measurement.TestRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductLabel>(entity =>
        {
            entity.ToTable(
                "ProductLabels",
                "mes",
                table => table.HasCheckConstraint(
                    "CK_ProductLabels_Status",
                    "[Status] IN ('Active', 'Voided', 'Replaced')"));
            entity.HasKey(label => label.Id);
            entity.Property(label => label.TemplateVersion).HasMaxLength(80);
            entity.Property(label => label.Printer).HasMaxLength(160);
            entity.Property(label => label.Status)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(label => label.Version).IsRowVersion();
            entity.HasIndex(label => new { label.ProductIdentityId, label.CreatedAtUtc });
            entity.HasOne(label => label.ProductIdentity)
                .WithMany()
                .HasForeignKey(label => label.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(label => label.ReplacesLabel)
                .WithMany()
                .HasForeignKey(label => label.ReplacesLabelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StartWipCommandReceipt>(entity =>
        {
            entity.ToTable(
                "StartWipCommandReceipts",
                "mes",
                table => table.HasTrigger("TR_StartWipCommandReceipts_AppendOnly"));
            entity.HasKey(receipt => receipt.Id);
            entity.Property(receipt => receipt.SourceSystem).HasMaxLength(80);
            entity.Property(receipt => receipt.IdempotencyKey).HasMaxLength(120);
            entity.Property(receipt => receipt.CommandHash).HasMaxLength(64);
            entity.Property(receipt => receipt.SerialNumber).HasMaxLength(200);
            entity.Property(receipt => receipt.ProductionOrderNumber).HasMaxLength(80);
            entity.Property(receipt => receipt.OrderStatus).HasMaxLength(24);
            entity.Property(receipt => receipt.IdentitySourceSystem).HasMaxLength(80);
            entity.Property(receipt => receipt.IdentitySourceType).HasMaxLength(32);
            entity.Property(receipt => receipt.NextOperationCode).HasMaxLength(80);
            entity.HasIndex(receipt => new { receipt.SourceSystem, receipt.IdempotencyKey })
                .IsUnique();
            entity.HasIndex(receipt => new { receipt.ProductIdentityId, receipt.CompletedAtUtc });
            entity.HasOne<ProductIdentity>()
                .WithMany()
                .HasForeignKey(receipt => receipt.ProductIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionOrder>()
                .WithMany()
                .HasForeignKey(receipt => receipt.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdentitySourceRegistration>(entity =>
        {
            entity.ToTable(
                "IdentitySourceRegistrations",
                "mes",
                table => table.HasCheckConstraint(
                    "CK_IdentitySourceRegistrations_DemoFlag",
                    "([SourceType] = 'DemoControlledPool' AND [IsDemo] = 1) OR ([SourceType] <> 'DemoControlledPool' AND [IsDemo] = 0)"));
            entity.HasKey(source => source.Id);
            entity.Property(source => source.SourceSystem).HasMaxLength(80);
            entity.Property(source => source.SourceType)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(source => source.AuthorizationEvidence).HasMaxLength(400);
            entity.HasIndex(source => source.SourceSystem).IsUnique();
            entity.HasOne(source => source.AuthorizedCallerUser)
                .WithMany()
                .HasForeignKey(source => source.AuthorizedCallerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(source => source.RegisteredByUser)
                .WithMany()
                .HasForeignKey(source => source.RegisteredByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdentitySourceIdentifierGrant>(entity =>
        {
            entity.ToTable("IdentitySourceIdentifierGrants", "mes");
            entity.HasKey(grant => new
            {
                grant.IdentitySourceRegistrationId,
                grant.IdentifierType,
            });
            entity.Property(grant => grant.IdentifierType)
                .HasConversion<string>()
                .HasMaxLength(24);
            entity.HasOne(grant => grant.IdentitySourceRegistration)
                .WithMany(source => source.IdentifierGrants)
                .HasForeignKey(grant => grant.IdentitySourceRegistrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationInboxMessage>(entity =>
        {
            entity.ToTable(
                "IntegrationInboxMessages",
                "integration",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_IntegrationInboxMessages_Status",
                        "[Status] IN ('Accepted', 'Rejected')");
                    table.HasCheckConstraint(
                        "CK_IntegrationInboxMessages_HttpStatusCode",
                        "[HttpStatusCode] BETWEEN 100 AND 599");
                });
            entity.HasKey(message => message.Id);
            entity.Property(message => message.SourceSystem).HasMaxLength(80);
            entity.Property(message => message.MessageId).HasMaxLength(120);
            entity.Property(message => message.MessageType).HasMaxLength(80);
            entity.Property(message => message.BusinessKey)
                .HasMaxLength(160)
                .IsRequired(false);
            entity.Property(message => message.SourceVersion)
                .HasMaxLength(80)
                .IsRequired(false);
            entity.Property(message => message.ContractVersion)
                .HasMaxLength(32)
                .IsRequired(false);
            entity.Property(message => message.PayloadHash).HasMaxLength(64);
            entity.Property(message => message.PayloadHashAlgorithm).HasMaxLength(16);
            entity.Property(message => message.PayloadJson)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);
            entity.Property(message => message.Status)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(message => message.ResultCode).HasMaxLength(80);
            entity.Property(message => message.ResultMessage).HasMaxLength(400);
            entity.HasIndex(message => new { message.SourceSystem, message.MessageId })
                .IsUnique();
            entity.HasIndex(message => new
            {
                message.SourceSystem,
                message.BusinessKey,
                message.SourceVersion,
            });
            entity.HasOne(message => message.ProductionOrder)
                .WithMany()
                .HasForeignKey(message => message.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntegrationInboxConflict>(entity =>
        {
            entity.ToTable("IntegrationInboxConflicts", "integration");
            entity.HasKey(conflict => conflict.Id);
            entity.Property(conflict => conflict.ExistingPayloadHash).HasMaxLength(64);
            entity.Property(conflict => conflict.ObservedPayloadHash).HasMaxLength(64);
            entity.Property(conflict => conflict.ResultCode).HasMaxLength(80);
            entity.Property(conflict => conflict.ResultMessage).HasMaxLength(400);
            entity.Property(conflict => conflict.CorrelationId).HasMaxLength(64);
            entity.HasIndex(conflict => new
            {
                conflict.InboxMessageId,
                conflict.OccurredAtUtc,
            });
            entity.HasOne(conflict => conflict.InboxMessage)
                .WithMany()
                .HasForeignKey(conflict => conflict.InboxMessageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ManufacturingEvent>(entity =>
        {
            entity.ToTable(
                "ManufacturingEvents",
                "mes",
                table => table.HasTrigger("TR_ManufacturingEvents_AppendOnly"));
            entity.HasKey(manufacturingEvent => manufacturingEvent.Id);
            entity.Property(manufacturingEvent => manufacturingEvent.EventType).HasMaxLength(80);
            entity.Property(manufacturingEvent => manufacturingEvent.AggregateType).HasMaxLength(80);
            entity.Property(manufacturingEvent => manufacturingEvent.AggregateId).HasMaxLength(160);
            entity.Property(manufacturingEvent => manufacturingEvent.Actor).HasMaxLength(160);
            entity.Property(manufacturingEvent => manufacturingEvent.PayloadJson).HasColumnType("nvarchar(max)");
            entity.Property(manufacturingEvent => manufacturingEvent.Location).HasMaxLength(120);
            entity.Property(manufacturingEvent => manufacturingEvent.CorrelationId).HasMaxLength(64);
            entity.HasIndex(manufacturingEvent => new
            {
                manufacturingEvent.AggregateType,
                manufacturingEvent.AggregateId,
                manufacturingEvent.OccurredAtUtc,
            });
            entity.HasIndex(manufacturingEvent => new
            {
                manufacturingEvent.ProductIdentityId,
                manufacturingEvent.RecordedAtUtc,
            });
            entity.HasIndex(manufacturingEvent => new
            {
                manufacturingEvent.ProductionOrderId,
                manufacturingEvent.RecordedAtUtc,
            });
            entity.HasOne(manufacturingEvent => manufacturingEvent.CorrectsEvent)
                .WithMany()
                .HasForeignKey(manufacturingEvent => manufacturingEvent.CorrectsEventId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(manufacturingEvent => manufacturingEvent.CausationEvent)
                .WithMany()
                .HasForeignKey(manufacturingEvent => manufacturingEvent.CausationEventId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static string RoleCheckSql(string columnName)
    {
        var values = string.Join(
            ", ",
            Enum.GetNames<BusinessRole>().Select(role => $"'{role}'"));
        return $"[{columnName}] IN ({values})";
    }
}
