namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Репозиторий кэша ранее присвоенных категорий (таблица commodity_category_assignments).
/// Масштаб кэша — сквозной для всех пользователей: методы НЕ принимают userId
/// (решение заказчика, ADR 009 решение C1/C4; UC-5).
/// </summary>
public interface ICategoryAssignmentRepository
{
    /// <summary>Точечный поиск по уникальному индексу normalized_name (FirstOrDefault).</summary>
    Task<CommodityCategoryAssignment?> GetByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);

    /// <summary>Вставка при отсутствии записи с таким normalized_name, иначе обновление (последняя подтверждённая категория выигрывает).</summary>
    Task UpsertAsync(
        string name,
        string normalizedName,
        CommodityCategory category,
        CancellationToken cancellationToken = default);
}