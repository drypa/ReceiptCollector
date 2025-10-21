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

        builder.HasIndex(merchant => merchant.Inn)
            .IsUnique();
    }
}
