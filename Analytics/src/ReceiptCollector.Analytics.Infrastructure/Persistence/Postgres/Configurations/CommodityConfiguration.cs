using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

internal sealed class CommodityConfiguration : IEntityTypeConfiguration<CommodityEntity>
{
    public void Configure(EntityTypeBuilder<CommodityEntity> builder)
    {
        builder.ToTable("commodities");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Quantity)
            .HasColumnType("numeric(18,3)");

        builder.Property(c => c.UnitPrice)
            .HasColumnType("numeric(18,2)");

        builder.Property(c => c.NdsSum)
            .HasColumnType("numeric(18,2)");

        builder.Property(c => c.CategoryName)
            .HasMaxLength(128);
    }
}
