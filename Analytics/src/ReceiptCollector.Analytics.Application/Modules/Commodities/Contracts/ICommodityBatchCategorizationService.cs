using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Порционная категоризация страницы «Товары» (ADR-028 п. 1.1/1.2/3/4/8):
/// stateless-сервер, клиент присылает подмножество товаров текущего пользователя.
/// «Страница» — понятие клиента; сервер состояния между запросами не держит.
/// </summary>
public interface ICommodityBatchCategorizationService
{
    /// <summary>
    /// Предлагает категории для порции товаров пользователя. Ничего не записывает
    /// (ни в <c>commodities</c>, ни в кэш, ни в очередь предкатегорий).
    /// <para>
    /// Строки с пустым названием и неизвестные/чужие id не попадают в конвейер; первые получают
    /// <c>undefined</c> с текстом ошибки, вторые просто отсутствуют в результате.
    /// </para>
    /// <para>
    /// При истечении бюджета <c>CommodityBatchCategorization:SuggestTimeout</c> отменяются только вызовы
    /// ИИ: незавершённые названия → <c>undefined</c> + текст таймаута, результат частичный (200).
    /// Отмена запроса клиентом прерывает всё (<see cref="OperationCanceledException"/>).
    /// </para>
    /// </summary>
    /// <param name="userId">Владелец товаров; выборка owner-scoped, id из тела не принимаются.</param>
    /// <param name="commodityIds">Идентификаторы строк порции (дубликаты дедуплицируются).</param>
    /// <exception cref="ArgumentException">Больше 20 уникальных названий в порции.</exception>
    Task<CommodityBatchCategorizationResult> SuggestAsync(
        Guid userId,
        IReadOnlyCollection<Guid> commodityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пакетно сохраняет assign-only изменения категорий товаров пользователя (ADR-028 п. 1.2):
    /// одна транзакция, возвращает число строк, у которых значение **реально изменилось**.
    /// <para>
    /// Кэш (ADR-019) пополняется только после успешной фиксации — <c>TryAdd</c> по нормализованному
    /// имени для каждой изменённой строки (first-wins/no-op). Очередь предкатегорий не меняется.
    /// </para>
    /// </summary>
    /// <param name="userId">Владелец товаров.</param>
    /// <param name="changes">Изменения; <see cref="Domain.Modules.Commodities.CommodityCategory.Undefined"/> запрещён.</param>
    /// <exception cref="ArgumentException">
    /// <c>Undefined</c>/неизвестная категория или дубликат <c>commodityId</c> в изменениях.
    /// </exception>
    /// <exception cref="Domain.Modules.Commodities.CommoditiesNotAccessibleException">
    /// Хотя бы один товар не найден среди товаров пользователя — ничего не записано.
    /// </exception>
    Task<int> ApplyAsync(
        Guid userId,
        IReadOnlyCollection<CommodityCategoryChange> changes,
        CancellationToken cancellationToken = default);
}
