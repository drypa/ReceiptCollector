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

        // ADR-034 п. 2 (B1): очищенный ключ строки очереди. NOT NULL (в SQL — после
        // backfill миграцией 20261109120000). Пишется только при вставке в TryAddProposalAsync.
        builder.Property(e => e.NormalizedName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.ProposedCategoryId)
            .IsRequired();

        // ADR-033 п. 1 (D1): неизменяемое исходное предложение; NOT NULL после backfill
        // в SQL-миграции 20261106120000. Пишется только фоновой вставкой строки.
        builder.Property(e => e.OriginalProposedCategoryId)
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

        // ADR-034 п. 2: поиск по очищенному ключу (любой статус) и pre-check кандидатов.
        // Индексы объявлены и в EF-модели, и в SQL-миграции 20261109120000: тесты создают
        // схему через EnsureCreatedAsync по EF-модели, прод — по SQL-скрипту.
        builder.HasIndex(e => e.NormalizedName)
            .HasDatabaseName("ix_precategorized_commodities_normalized_name");

        // Один proposed на очищенный ключ глобально (FR-3.4 ADR-027 по очищенному ключу);
        // applied в частичный индекс не входят.
        builder.HasIndex(e => e.NormalizedName)
            .IsUnique()
            .HasFilter("status = 0")
            .HasDatabaseName("ux_precategorized_commodities_normalized_proposed");
    }
}
