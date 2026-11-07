namespace ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

/// <summary>
/// Параметры серверной выборки уникальных сочетаний журнала (ADR-033 п. 7, T-07).
/// Расположен в Domain, потому что контракт <see cref="ICategorizationDecisionRepository"/>
/// принимает его как параметр, а Domain по архитектурному правилу не ссылается на Application;
/// Application (контракт read-сервиса) использует этот же тип — ссылка Application → Domain разрешена.
/// <para>
/// Семантика (ADR-033 «Явно зафиксированные семантики»): фильтры применяются к событиям,
/// затем дедуп по сочетанию; период (<see cref="From"/>/<see cref="To"/>) суживает и «последнее»
/// событие сочетания. Ключ сочетания — <c>(commodity_name, suggestion_source,
/// suggested_category_id, final_category_id)</c>; «Хлеб»/«ХЛЕБ» — разные строки выдачи.
/// </para>
/// </summary>
/// <param name="OnlyMismatch">
/// <c>true</c> — только расхождения (<c>suggested_category_id &lt;&gt; final_category_id</c>).
/// </param>
/// <param name="Source">Wire-имя источника предложения (одно из четырёх); <see langword="null"/> — без фильтра.</param>
/// <param name="SuggestedCategoryId">Код предложенной категории; <see langword="null"/> — без фильтра.</param>
/// <param name="FinalCategoryId">Код итоговой категории (0 = «Не указана» допустим); <see langword="null"/> — без фильтра.</param>
/// <param name="Search">
/// Подстрока по названию: <c>ILIKE '%term%'</c> по исходному ИЛИ нормализованному имени
/// без учёта регистра (прецедент очереди ADR-027). Пустое/пробельное — без фильтра.
/// </param>
/// <param name="From">Нижняя граница периода по событиям: <c>created_at &gt;= From</c>.</param>
/// <param name="To">Верхняя граница периода по событиям: <c>created_at &lt; To</c> (исключающая).</param>
/// <param name="Limit">Размер страницы (должен быть &gt;= 1; HTTP-валидация диапазона — T-08).</param>
/// <param name="Offset">Смещение страницы (должен быть &gt;= 0).</param>
public sealed record CategorizationDecisionQuery(
    bool OnlyMismatch,
    string? Source,
    int? SuggestedCategoryId,
    int? FinalCategoryId,
    string? Search,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit,
    int Offset);
