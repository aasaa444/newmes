using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Persistence;

public sealed class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    public DbSet<BusinessAuditRecord> BusinessAuditRecords => Set<BusinessAuditRecord>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();

    public DbSet<ManufacturingEvent> ManufacturingEvents => Set<ManufacturingEvent>();

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
            entity.Property(material => material.TraceabilityMode)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.HasIndex(material => material.Code).IsUnique();
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
                        "[Status] IN ('Created', 'Released', 'Closed', 'Cancelled')");
                });
            entity.HasKey(order => order.Id);
            entity.Property(order => order.OrderNumber).HasMaxLength(80);
            entity.Property(order => order.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
            entity.Property(order => order.SourceSystem).HasMaxLength(80);
            entity.Property(order => order.SourceReference).HasMaxLength(160);
            entity.Property(order => order.Version).IsRowVersion();
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasOne(order => order.Material)
                .WithMany()
                .HasForeignKey(order => order.MaterialId)
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
            entity.HasIndex(manufacturingEvent => new
            {
                manufacturingEvent.AggregateType,
                manufacturingEvent.AggregateId,
                manufacturingEvent.OccurredAtUtc,
            });
            entity.HasOne(manufacturingEvent => manufacturingEvent.CorrectsEvent)
                .WithMany()
                .HasForeignKey(manufacturingEvent => manufacturingEvent.CorrectsEventId)
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
