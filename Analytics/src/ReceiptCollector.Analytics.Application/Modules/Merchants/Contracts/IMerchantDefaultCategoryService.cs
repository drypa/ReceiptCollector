using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;

/// <summary>
/// Дожим исторических позиций магазина товарной категорией по умолчанию (ADR-032, п. 4,
/// E1/FR-M3) — операция кнопки «Категоризировать существующие». Контракт в Application,
/// реализация в Infrastructure (прецедент <see cref="Commodities.Contracts.IPrecategorizedCommodityService"/>).
/// <para>
/// Ожидаемые бизнес-состояния («магазин не найден», «дефолт не задан») возвращаются исходом
/// <see cref="MerchantDefaultApplyOutcome"/>, а не исключениями; исключения инфраструктуры
/// (сбой БД) пробрасываются как есть — их перехватывает глобальное правило запроса (500),
/// а не сервис (ADR-032, п. 4).
/// </para>
/// <para>
/// Права (401/403) проверяет эндпоинт (T-07), как во всех методах <c>MerchantEndpoints</c>;
/// <paramref name="adminUserId"/> используется для аудита операции, а не для owner-scoping:
/// дожим обрабатывает позиции всех пользователей (E1). Сервис не использует
/// <c>IgnoreQueryFilters</c> сам — глобальный доступ инкапсулирован в репозиторной операции
/// (NFR-5, стиль ADR-027) — и не пишет в <c>precategorized_commodities</c>: пересечение с
/// фоновой сессией предкатегоризации безопасно (ADR-032, п. 4).
/// </para>
/// </summary>
public interface IMerchantDefaultCategoryService
{
    /// <summary>
    /// Применяет дефолтную товарную категорию магазина ко всем его позициям <b>без категории</b>
    /// (<c>category_id IS NULL OR = 0</c>), всех пользователей. Содержательные категории не
    /// перезаписываются (FR-M5); повторный вызов идемпотентен — <c>Applied</c> с
    /// <see cref="MerchantDefaultApplyResult.UpdatedCount"/> = 0 считается успехом.
    /// <para>
    /// Порядок: загрузка магазина (без <c>IgnoreQueryFilters</c> — merchants не фильтруются) →
    /// проверка настроенного дефолта (невалидный код из БД безопасно прочитан как <c>null</c>,
    /// A1) → репозиторная операция <c>ApplyMerchantDefaultForMerchantAsync</c>: транзакция,
    /// UPDATE с <c>category_source = 'merchant-default'</c> и пополнение in-memory кэша после
    /// фиксации — внутри репозитория (T-04), на уровне сервиса не дублируется.
    /// </para>
    /// </summary>
    /// <param name="adminUserId">Идентификатор администратора-инициатора (аудит; права проверяет эндпоинт).</param>
    /// <param name="merchantId">Магазин, к позициям которого применяется дефолт.</param>
    /// <param name="cancellationToken">Отмена запроса.</param>
    Task<MerchantDefaultApplyResult> ApplyExistingAsync(
        Guid adminUserId,
        Guid merchantId,
        CancellationToken cancellationToken = default);
}
