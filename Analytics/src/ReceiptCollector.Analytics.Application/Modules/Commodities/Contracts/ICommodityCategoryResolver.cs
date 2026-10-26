using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Общий конвейер категоризации товаров (ADR-028 п. 9): <c>existing → cache → precategorized → ai</c>.
/// Один конвейер на два сценария — suggest чека (ADR-009) и порционный suggest страницы «Товары»
/// (ADR-028). Шаги, приоритеты, дедупликация по нормализованному имени и тексты ошибок
/// зафиксированы ADR-009/ADR-019/ADR-027/ADR-028.
/// </summary>
public interface ICommodityCategoryResolver
{
    /// <summary>
    /// Категоризирует переданные товары. Ничего не сохраняет и не кэширует (read-only):
    /// запись в кэш выполняется только при подтверждении пользователем (ADR-019).
    /// </summary>
    /// <param name="items">Товары для категоризации (порядок результата повторяет шаги конвейера).</param>
    /// <param name="aiEnabled">
    /// Вызывать ли ИИ: <c>false</c> (пустой <c>AI:BaseUrl</c>) — ИИ не вызывается, остаток
    /// помечается <c>undefined</c> + «ИИ не настроен.» (ADR-028 п. 4).
    /// </param>
    /// <param name="aiCancellationToken">
    /// Токен только для вызовов ИИ (может нести серверный дедлайн запроса, ADR-028 п. 3).
    /// Его отмена не прерывает весь запрос — незавершённые названия становятся <c>undefined</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Токен запроса клиента: его отмена прерывает всё (<see cref="OperationCanceledException"/>
    /// пробрасывается, частичный результат не возвращается).
    /// </param>
    Task<IReadOnlyList<ReceiptItemCategorizationResult>> ResolveAsync(
        IReadOnlyCollection<Commodity> items,
        bool aiEnabled,
        CancellationToken aiCancellationToken,
        CancellationToken cancellationToken);
}
