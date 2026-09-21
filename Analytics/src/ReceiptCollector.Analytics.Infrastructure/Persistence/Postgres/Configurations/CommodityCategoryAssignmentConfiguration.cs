using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

internal sealed class CommodityCategoryAssignmentConfiguration : IEntityTypeConfiguration<CommodityCategoryAssignmentEntity>
{
    public void Configure(EntityTypeBuilder<CommodityCategoryAssignmentEntity> builder)
    {
        builder.ToTable("commodity_category_assignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.NormalizedName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.CategoryName)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(a => a.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_commodity_category_assignments_normalized_name");
    }
}