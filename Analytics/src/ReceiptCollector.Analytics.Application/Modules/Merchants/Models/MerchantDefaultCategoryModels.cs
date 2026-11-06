namespace ReceiptCollector.Analytics.Application.Modules.Merchants.Models;

/// <summary>
/// Исход дожима исторических позиций магазина категорией по умолчанию (ADR-032, п. 4, E1/FR-M3).
/// Ожидаемые бизнес-состояния админского сценария возвращаются значением, а не исключениями
/// (по образцу <c>PrecategorizedOperationOutcome</c>, ADR-027); исключения — только сбои
/// инфраструктуры (глобальное правило запроса → 500).
/// </summary>
public enum MerchantDefaultApplyOutcome
{
    /// <summary>
    /// Дожим выполнен: позиции без категории получили дефолт. <c>UpdatedCount</c> может быть 0 —
    /// это успех (идемпотентность, повторное нажатие кнопки).
    /// </summary>
    Applied = 0,

    /// <summary>Магазин не найден → HTTP 404 (кнопка нажата по устаревшему списку магазинов).</summary>
    NotFound = 1,

    /// <summary>
    /// Дефолтная товарная категория не задана — включая невалидный код из БД, приведённый к
    /// <c>null</c> безопасным чтением (A1) → HTTP 400 текстом.
    /// </summary>
    DefaultNotConfigured = 2
}

/// <summary>
/// Результат дожима (<see cref="Contracts.IMerchantDefaultCategoryService.ApplyExistingAsync"/>):
/// форма зафиксирована ADR-032 (amend открытого вопроса 2 декомпозиции) по образцу
/// <c>ConfirmPrecategorizedResult</c> (ADR-027) — outcome + число обновлённых строк.
/// Маппинг в HTTP (T-07): <c>NotFound → 404</c>, <c>DefaultNotConfigured → 400</c> текстом,
/// <c>Applied → 200 { updated }</c> (в т.ч. <c>updated = 0</c>).
/// </summary>
/// <param name="Outcome">Исход операции.</param>
/// <param name="UpdatedCount">
/// Число позиций, получивших категорию (только при <see cref="MerchantDefaultApplyOutcome.Applied"/>;
/// 0 — все позиции уже категоризированы). Для не-успешных исходов — 0.
/// </param>
public sealed record MerchantDefaultApplyResult(
    MerchantDefaultApplyOutcome Outcome,
    int UpdatedCount);
