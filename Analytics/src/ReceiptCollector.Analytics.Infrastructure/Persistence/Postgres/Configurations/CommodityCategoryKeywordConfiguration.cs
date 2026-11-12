using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

/// <summary>
/// Конфигурация слов категоризации (ADR-035 п. 1, решение A1).
/// Схема дублируется в SQL-миграции <c>20261112120000_add_commodity_category_keywords</c>:
/// Postgres-тесты создают схему из EF-модели (<c>EnsureCreatedAsync</c>), поэтому имена таблицы
/// и колонок snake_case, длины, три CHECK и уникальный индекс объявлены и здесь — иначе тестовая
/// схема не содержала бы инвариантов, которые проверяют тесты (прецеденты ADR-033/034).
/// </summary>
internal sealed class CommodityCategoryKeywordConfiguration
    : IEntityTypeConfiguration<CommodityCategoryKeywordEntity>
{
    public void Configure(EntityTypeBuilder<CommodityCategoryKeywordEntity> builder)
    {
        builder.ToTable("commodity_category_keywords", table =>
        {
            // FR-1: категория — код enum без справочника в БД (ADR-005); Undefined (0) запрещён,
            // Other (255) допустим.
            table.HasCheckConstraint(
                "ck_commodity_category_keywords_category_not_undefined",
                "category_id <> 0");

            // FR-1: множество типов списка закрыто — ровно два списка (boost | exclude).
            table.HasCheckConstraint(
                "ck_commodity_category_keywords_list_type",
                "list_type IN ('boost', 'exclude')");

            // FR-5: длина слова после trim — 2–64 символа (проверка сервиса дублирует правило
            // ради адресного 400, схема — последний рубеж).
            table.HasCheckConstraint(
                "ck_commodity_category_keywords_word_length",
                "char_length(word) BETWEEN 2 AND 64");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CategoryId)
            .IsRequired();

        builder.Property(e => e.ListType)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.Word)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.NormalizedWord)
            .IsRequired()
            .HasMaxLength(64);

        // FR-5: дубликат в рамках «одна категория + один список» (регистр не важен).
        // Индекс же обслуживает per-category чтение (префикс category_id, list_type).
        builder.HasIndex(e => new { e.CategoryId, e.ListType, e.NormalizedWord })
            .IsUnique()
            .HasDatabaseName("ux_commodity_category_keywords_category_list_word");
    }
}
