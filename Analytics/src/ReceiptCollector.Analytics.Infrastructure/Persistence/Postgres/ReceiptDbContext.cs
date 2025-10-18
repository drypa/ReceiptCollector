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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new CommodityConfiguration());
    }
}
