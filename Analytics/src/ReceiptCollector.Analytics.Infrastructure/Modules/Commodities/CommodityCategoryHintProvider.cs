using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Единственная реализация доставки подсказок (ADR-035, решение C1, п. 4.2): снимок из
/// singleton-кэша <see cref="ICommodityCategoryKeywordCache"/> (на горячем пути — без БД и
/// без блокировок, NFR-1) + чистый матчер <see cref="CategoryKeywordMatcher.GetHints"/>.
/// Про БД и про формат промта провайдер не знает: он не читает репозитории и не участвует
/// в рендеринге подсказок (это задача 06 — <c>OpenAiCompatibleAiClient</c>).
/// Регистрация — Scoped (<c>DependencyInjectionExtensions</c>): состояние всё в singleton-кэше,
/// провайдер stateless.
/// </summary>
internal sealed class CommodityCategoryHintProvider : ICommodityCategoryHintProvider
{
    private readonly ICommodityCategoryKeywordCache _cache;

    public CommodityCategoryHintProvider(ICommodityCategoryKeywordCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public IReadOnlyList<CommodityCategoryKeywordHint> GetHints(string normalizedName)
    {
        var matched = CategoryKeywordMatcher.GetHints(_cache.GetSnapshot(), normalizedName);
        if (matched.Count == 0)
        {
            return Array.Empty<CommodityCategoryKeywordHint>();
        }

        // Порядок (exclude → boost) пришёл уже отсортированным из матчера — здесь только
        // проекция домена в контракт Application, пересортировки нет (ADR-035 п. 4.1).
        var hints = new List<CommodityCategoryKeywordHint>(matched.Count);
        foreach (var keyword in matched)
        {
            hints.Add(new CommodityCategoryKeywordHint(keyword.Category, keyword.ListType, keyword.Word));
        }

        return hints;
    }
}
