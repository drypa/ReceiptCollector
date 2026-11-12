using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Единственная точка доставки подсказок по ключевым словам обоим вызывающим — resolver'у
/// ручного suggest и фону прекатегоризации (ADR-035, решение C1, FR-6/FR-8): матчинг существует
/// ровно в одном месте (<c>CategoryKeywordMatcher</c> в Domain), доставка — ровно здесь.
/// Реализация — Infrastructure (<c>CommodityCategoryHintProvider</c> поверх singleton-кэша
/// <see cref="ICommodityCategoryKeywordCache"/>): снимок из кэша + вызов матчера; про БД и про
/// промт провайдер не знает (NFR-1 по построению).
/// <para>
/// Подсказки — данные запроса, а не инструкция: решение о категории принимает модель
/// (ADR-035, «Явно зафиксированные семантики»: в промт попадает только совпавшее).
/// </para>
/// </summary>
public interface ICommodityCategoryHintProvider
{
    /// <summary>
    /// Подсказки по уже нормализованному названию товара
    /// (<see cref="Domain.Modules.Commodities.CommodityNameNormalizer.NormalizeName"/> — вход
    /// матчера, дополнительная нормализация не выполняется).
    /// </summary>
    /// <param name="normalizedName">Очищенное название; пустое/без букв — совпадений нет.</param>
    /// <returns>
    /// Все совпавшие подсказки в детерминированном порядке (exclude первыми, затем boost;
    /// внутри — код категории asc, слово ordinal asc — порядок задаёт матчер). Пустая коллекция,
    /// если совпадений нет; <c>null</c> не возвращается.
    /// </returns>
    IReadOnlyList<CommodityCategoryKeywordHint> GetHints(string normalizedName);
}
