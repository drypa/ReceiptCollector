using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

internal sealed class MerchantConfiguration : IEntityTypeConfiguration<MerchantEntity>
{
    public void Configure(EntityTypeBuilder<MerchantEntity> builder)
    {
        builder.ToTable("merchants");
        builder.HasKey(merchant => merchant.Id);

        builder.Property(merchant => merchant.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(merchant => merchant.Category)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(merchant => merchant.Address)
            .HasMaxLength(512);

        builder.Property(merchant => merchant.Inn)
            .HasMaxLength(16);

        // Код CommodityCategory (колонка default_commodity_category_id, ADR-032 A1).
        // CHECK «IS NULL OR <> 0» объявлен только в SQL-миграции
        // 20261105130000_add_merchant_default_commodity_category (прецедент ADR-027 п.3);
        // невалидные коды нейтрализует безопасное чтение в MerchantEntity.MapToDomain.
        builder.Property(merchant => merchant.DefaultCommodityCategoryId);

        builder.HasIndex(merchant => merchant.Inn)
            .IsUnique();
    }
}
