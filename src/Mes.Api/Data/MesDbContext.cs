using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

public class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<BusinessAuditEntry> AuditEntries => Set<BusinessAuditEntry>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<ProcessRoute> ProcessRoutes => Set<ProcessRoute>();
    public DbSet<ProcessStep> ProcessSteps => Set<ProcessStep>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<WorkStation> WorkStations => Set<WorkStation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
