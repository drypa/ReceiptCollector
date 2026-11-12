namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Доступ к словам категоризации (<c>commodity_category_keywords</c>, ADR-035 п. 1, решение A1).
/// Категория — код enum без FK (ADR-005): имена и группы в БД не денормализуются.
/// Чтение снимка для матчинга — <see cref="GetAllAsync"/> (кэш вызывает его только при
/// прогреве/инвалидации, NFR-1/NFR-4).
/// </summary>
public interface ICommodityCategoryKeywordRepository
{
    /// <summary>
    /// Полный снимок слов всех категорий и списков одним запросом. Порядок строк не определён:
    /// детерминированный порядок подсказок задаёт <see cref="CategoryKeywordMatcher"/>.
    /// </summary>
    Task<IReadOnlyCollection<CommodityCategoryKeyword>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Есть ли слово в конкретной паре «категория + список» (проверка case-insensitive дубликата
    /// по <paramref name="normalizedWord"/>, FR-5). Слово в другой категории или другом списке
    /// ответ не меняет — проверка адресная.
    /// </summary>
    Task<bool> ExistsAsync(
        CommodityCategory category,
        KeywordListType listType,
        string normalizedWord,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Количество слов в конкретной паре «категория + список» (проверка лимита
    /// <see cref="CommodityCategoryKeywordRules.MaxWordsPerList"/> перед вставкой, FR-5).
    /// </summary>
    Task<int> CountAsync(
        CommodityCategory category,
        KeywordListType listType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Вставка слова. <c>false</c> — конфликт уникального индекса
    /// <c>(category_id, list_type, normalized_word)</c> (23505): гонка двух вкладок даёт
    /// <c>409</c>, а не <c>500</c> (ADR-035 п. 7). Исключение не пробрасывается.
    /// </summary>
    Task<bool> AddAsync(CommodityCategoryKeyword keyword, CancellationToken cancellationToken = default);

    /// <summary>Удаление слова по <c>id</c>. <c>false</c> — строки нет (уже удалена в другой вкладке).</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
