using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class ReceiptDbContext : DbContext
{
    public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReceiptEntity> Receipts => Set<ReceiptEntity>();
    public DbSet<CommodityEntity> Commodities => Set<CommodityEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    internal Guid? CurrentUserId { get; private set; }

    internal void SetCurrentUser(Guid userId)
    {
        CurrentUserId = userId;
    }

    internal void ClearCurrentUser()
    {
        CurrentUserId = null;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new CommodityConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.Entity<ReceiptEntity>()
            .HasQueryFilter(r => CurrentUserId == null || r.UserId == CurrentUserId);
    }
}
