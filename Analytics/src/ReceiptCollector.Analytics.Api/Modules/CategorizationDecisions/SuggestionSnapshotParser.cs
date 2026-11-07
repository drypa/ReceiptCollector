using System.Globalization;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Modules.CategorizationDecisions;

/// <summary>
/// Состояние клиентского снимка предложения в элементе сохранения (ADR-033 п. 3).
/// </summary>
internal enum SuggestionSnapshotKind
{
    /// <summary>Оба поля пусты — снимка нет (FR-5b).</summary>
    Absent,

    /// <summary>Снимок валиден: предложенная категория и источник допустимы.</summary>
    Valid,

    /// <summary>Снимок прислан частично/невалидно — игнорируется без ошибки запроса.</summary>
    Invalid
}

/// <summary>
/// Общий разбор клиентского снимка предложения для обоих save-эндпоинтов — чека
/// (<c>PUT /api/receipts/{id}/categories</c>, ADR-033 п. 3) и страницы «Товары»
/// (<c>PUT /api/commodities/categories</c>, ADR-033 п. 4). Правила вынесены в один helper
/// (план T-05), чтобы семантика снимка в двух контрактах не разъехалась.
/// </summary>
internal static class SuggestionSnapshotParser
{
    /// <summary>
    /// Разбор снимка предложения (ADR-033 п. 3.1–3.2): оба поля пусты — снимка нет; иначе снимок
    /// валиден только при непустой, не числовой, определённой и не <c>Undefined</c> категории и
    /// источнике из четырёх допустимых wire-имён (ADR-033 п. 6). Валидацию формы события всё равно
    /// повторяет recorder — здесь отбрасывается только заведомо невалидный клиентский снимок.
    /// Невалидный снимок не является ошибкой запроса (NFR-2): вызывающий эндпоинт отбрасывает его
    /// с <c>Warning</c> (только количество) и продолжает сохранение.
    /// </summary>
    internal static SuggestionSnapshotKind Parse(
        string? suggestedCategory,
        string? suggestionSource,
        out CommodityCategory category,
        out string source)
    {
        category = CommodityCategory.Undefined;
        source = string.Empty;

        if (string.IsNullOrWhiteSpace(suggestedCategory) && string.IsNullOrWhiteSpace(suggestionSource))
        {
            return SuggestionSnapshotKind.Absent;
        }

        if (string.IsNullOrWhiteSpace(suggestedCategory)
            || string.IsNullOrWhiteSpace(suggestionSource)
            || int.TryParse(suggestedCategory, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return SuggestionSnapshotKind.Invalid;
        }

        if (!Enum.TryParse(suggestedCategory, ignoreCase: true, out category)
            || !Enum.IsDefined(category)
            || category == CommodityCategory.Undefined
            || !CategorizationDecisionSources.IsAllowed(suggestionSource))
        {
            category = CommodityCategory.Undefined;
            return SuggestionSnapshotKind.Invalid;
        }

        source = suggestionSource;
        return SuggestionSnapshotKind.Valid;
    }
}
