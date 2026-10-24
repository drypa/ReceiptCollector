using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

/// <summary>
/// Конфигурация очереди предкатегоризации (ADR-027 п.3).
/// Индексы объявляются и в EF-модели, и в SQL-миграции: тесты создают схему через
/// <c>Database.EnsureCreatedAsync()</c> по EF-модели, прод — по SQL-скрипту (замечание 3 плана).
/// Индексы на <c>commodities</c> НЕ создаются (NFR-6, отложенное решение).
/// </summary>
internal sealed class PrecategorizedCommodityConfiguration : IEntityTypeConfiguration<PrecategorizedCommodityEntity>
{
    public void Configure(EntityTypeBuilder<PrecategorizedCommodityEntity> builder)
    {
        builder.ToTable("precategorized_commodities");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.ProposedCategoryId)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<short>()
            .IsRequired()
            .HasDefaultValue(PrecategorizedCommodityStatus.Proposed);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        builder.Property(e => e.AppliedAt)
            .HasColumnType("timestamp with time zone");

        // Сознательно CASCADE, в отличие от RESTRICT у receipts (ADR-027 п.3):
        // строка предложения — производная данных, удаление магазина не должно блокироваться.
        builder.HasOne(e => e.Merchant)
            .WithMany()
            .HasForeignKey(e => e.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.Name, e.MerchantId })
            .IsUnique()
            .HasDatabaseName("ux_precategorized_commodities_name_merchant");

        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_precategorized_commodities_status");
    }
}
