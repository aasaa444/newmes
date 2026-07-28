using Mes.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Data;

public class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<BusinessAuditEntry> AuditEntries => Set<BusinessAuditEntry>();

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
    }
}
