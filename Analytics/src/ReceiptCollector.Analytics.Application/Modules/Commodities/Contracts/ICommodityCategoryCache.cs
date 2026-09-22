using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// In-memory кэш категорий товаров (ADR 019). Ключ — нормализованное название товара,
/// значение — категория CommodityCategory. Масштаб — сквозной для всех пользователей (без userId, FR-2.4).
/// Реализация — Infrastructure (InMemoryCommodityCategoryCache), регистрация Singleton.
/// </summary>
public interface ICommodityCategoryCache
{
    /// <summary>Текущее число записей в кэше.</summary>
    int Count { get; }

    /// <summary>Поиск категории по нормализованному названию (FR-2.2, шаг 2 алгоритма).</summary>
    bool TryGet(string normalizedName, out CommodityCategory category);

    /// <summary>
    /// Добавление записи. First-wins: если ключ уже есть — no-op (FR-1.4).
    /// При Count ≥ MaxSize (лимит достигнут) — no-op: кэш «замерзает», вытеснения нет (FR-1.5).
    /// Undefined не добавляется (FR-1.2).
    /// Возвращает true, если запись добавлена.
    /// </summary>
    bool TryAdd(string normalizedName, CommodityCategory category);
}