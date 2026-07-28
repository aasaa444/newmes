using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

/// <summary>
/// MES 统一 DbContext（第一期单体）。
/// 含：身份/审计 + 执行主数据 + 工单/线边库存（票 03）。
/// 生产默认 SQL Server；集成测试由 MesApiFactory 换成 SQLite 内存库。
/// </summary>
public class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    // ----- 票 01：身份与审计 -----
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<BusinessAuditEntry> AuditEntries => Set<BusinessAuditEntry>();

    // ----- 票 02：执行主数据 -----
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<ProcessRoute> ProcessRoutes => Set<ProcessRoute>();
    public DbSet<ProcessStep> ProcessSteps => Set<ProcessStep>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<WorkStation> WorkStations => Set<WorkStation>();

    // ----- 票 03：工单与线边 -----
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderIssueLine> WorkOrderIssueLines => Set<WorkOrderIssueLine>();
    public DbSet<LineSideInventory> LineSideInventories => Set<LineSideInventory>();

    // ----- 票 04：SN / 过站 / 谱系 -----
    public DbSet<ProductSerial> ProductSerials => Set<ProductSerial>();
    public DbSet<SerialPassRecord> SerialPassRecords => Set<SerialPassRecord>();
    public DbSet<ComponentBinding> ComponentBindings => Set<ComponentBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 约束与索引集中在此；业务规则仍在端点/领域服务中。
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<BusinessAuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorUserName).HasMaxLength(64).IsRequired();
            e.Property(x => x.SubjectType).HasMaxLength(64);
            e.Property(x => x.SubjectId).HasMaxLength(128);
            e.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<Material>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Bom>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).HasMaxLength(16).IsRequired();
            e.HasOne(x => x.FinishedMaterial).WithMany().HasForeignKey(x => x.FinishedMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(x => x.Bom!).HasForeignKey(x => x.BomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BomLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.QuantityPer).HasPrecision(18, 4);
            e.HasOne(x => x.ComponentMaterial).WithMany().HasForeignKey(x => x.ComponentMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProcessRoute>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Version).HasMaxLength(16).IsRequired();
            e.HasOne(x => x.FinishedMaterial).WithMany().HasForeignKey(x => x.FinishedMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Steps).WithOne(x => x.ProcessRoute!).HasForeignKey(x => x.ProcessRouteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.ProcessRouteId, x.Sequence }).IsUnique();
        });

        modelBuilder.Entity<ProductionLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasMany(x => x.Stations).WithOne(x => x.ProductionLine!).HasForeignKey(x => x.ProductionLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkStation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasOne(x => x.BoundProcessStep).WithMany().HasForeignKey(x => x.BoundProcessStepId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.Property(x => x.OrderNo).HasMaxLength(64).IsRequired();
            e.Property(x => x.PlannedQty).HasPrecision(18, 4);
            e.Property(x => x.CompletedQty).HasPrecision(18, 4);
            e.Property(x => x.ScrappedQty).HasPrecision(18, 4);
            e.Property(x => x.FrozenRouteVersion).HasMaxLength(16);
            e.Property(x => x.FrozenBomVersion).HasMaxLength(16);
            e.HasOne(x => x.FinishedMaterial).WithMany().HasForeignKey(x => x.FinishedMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ProcessRoute).WithMany().HasForeignKey(x => x.ProcessRouteId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Bom).WithMany().HasForeignKey(x => x.BomId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.IssueLines).WithOne(x => x.WorkOrder!).HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkOrderIssueLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IssuedQty).HasPrecision(18, 4);
            e.Property(x => x.PendingQty).HasPrecision(18, 4);
            e.Property(x => x.ConsumedQty).HasPrecision(18, 4);
            e.HasIndex(x => new { x.WorkOrderId, x.MaterialId }).IsUnique();
            e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LineSideInventory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MaterialId).IsUnique();
            e.Property(x => x.QuantityOnHand).HasPrecision(18, 4);
            e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductSerial>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SerialNo).IsUnique();
            e.Property(x => x.SerialNo).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.PassRecords).WithOne(x => x.ProductSerial!).HasForeignKey(x => x.ProductSerialId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ComponentBindings).WithOne(x => x.ProductSerial!).HasForeignKey(x => x.ProductSerialId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SerialPassRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Result).HasMaxLength(16).IsRequired();
            e.Property(x => x.OperatorUserName).HasMaxLength(64);
            e.HasOne(x => x.ProcessStep).WithMany().HasForeignKey(x => x.ProcessStepId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkStation).WithMany().HasForeignKey(x => x.WorkStationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ComponentBinding>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ComponentSerialNo).IsUnique();
            e.Property(x => x.ComponentSerialNo).HasMaxLength(64).IsRequired();
            e.Property(x => x.OperatorUserName).HasMaxLength(64);
            e.HasOne(x => x.ComponentMaterial).WithMany().HasForeignKey(x => x.ComponentMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
