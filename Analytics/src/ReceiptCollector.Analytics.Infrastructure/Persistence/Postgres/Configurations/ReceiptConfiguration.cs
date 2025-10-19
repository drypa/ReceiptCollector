using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<ReceiptEntity>
{
    public void Configure(EntityTypeBuilder<ReceiptEntity> builder)
    {
        builder.ToTable("receipts");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Merchant)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.ExternalId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.TotalAmount)
            .HasColumnType("numeric(18,2)");

        builder.Property(r => r.PurchasedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasMany(r => r.Items)
            .WithOne(i => i.Receipt)
            .HasForeignKey(i => i.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
