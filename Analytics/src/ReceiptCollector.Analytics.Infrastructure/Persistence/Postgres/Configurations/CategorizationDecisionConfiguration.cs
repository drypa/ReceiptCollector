using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

/// <summary>
/// Конфигурация журнала решений категоризации (ADR-033 п. 1–2).
/// Схема повторяет SQL-миграцию <c>20261106120000_add_categorization_decisions</c>: имена таблицы
/// и колонок snake_case, типы/длины, оба CHECK и все три индекса объявлены и здесь, потому что
/// Postgres-тесты создают схему из EF-модели (<c>EnsureCreatedAsync</c>).
/// </summary>
internal sealed class CategorizationDecisionConfiguration : IEntityTypeConfiguration<CategorizationDecisionEntity>
{
    public void Configure(EntityTypeBuilder<CategorizationDecisionEntity> builder)
    {
        builder.ToTable("categorization_decisions", table =>
        {
            table.HasCheckConstraint(
                "ck_categorization_decisions_suggested_not_undefined",
                "suggested_category_id <> 0");
            table.HasCheckConstraint(
                "ck_categorization_decisions_final_non_negative",
                "final_category_id >= 0");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CommodityName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.NormalizedName)
            .IsRequired()
            .HasMaxLength(256);

        // Допустимое множество источников — свойство кода (recorder, T-03), CHECK по перечню
        // осознанно не ставится: расширение источников не должно требовать миграции.
        builder.Property(e => e.SuggestionSource)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(e => e.SuggestedCategoryId)
            .IsRequired();

        builder.Property(e => e.FinalCategoryId)
            .IsRequired();

        builder.Property(e => e.AcceptedBy)
            .IsRequired();

        builder.Property(e => e.AcceptedByAdmin)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        // Дедуп-выборка (E1) и группировки сочетаний: ключ сочетания + порядок
        // «самое свежее первым» (created_at DESC, id DESC).
        builder.HasIndex(e => new
            {
                e.CommodityName,
                e.SuggestionSource,
                e.SuggestedCategoryId,
                e.FinalCategoryId,
                e.CreatedAt,
                e.Id
            })
            .HasDatabaseName("ix_categorization_decisions_combination")
            .IsDescending(false, false, false, false, true, true);

        // На одну колонку создаются два разных индекса, поэтому имя модели задаётся в HasIndex,
        // а имя в БД — отдельно через HasDatabaseName: UseSnakeCaseNamingConvention генерирует
        // database name из таблицы и колонок, игнорируя имя модели (иначе получился бы ..._created_at1).

        // Порядок списка по умолчанию, фильтр периода, будущая ретенция.
        builder.HasIndex(e => e.CreatedAt, "ix_categorization_decisions_created_at")
            .HasDatabaseName("ix_categorization_decisions_created_at")
            .IsDescending(true);

        // Основной фильтр админской страницы: «только расхождения».
        builder.HasIndex(e => e.CreatedAt, "ix_categorization_decisions_mismatch")
            .HasDatabaseName("ix_categorization_decisions_mismatch")
            .IsDescending(true)
            .HasFilter("suggested_category_id <> final_category_id");
    }
}
