using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Persistence;

public sealed class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();

    public DbSet<ManufacturingEvent> ManufacturingEvents => Set<ManufacturingEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("UserAccounts", "security");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Username).HasMaxLength(80);
            entity.Property(account => account.DisplayName).HasMaxLength(120);
            entity.HasIndex(account => account.Username).IsUnique();
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
}
