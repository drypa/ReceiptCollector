using System.Collections.Frozen;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Wire-имена исходов строки массовой операции над очередью предкатегоризации
/// (ADR-036 §1.1/§1.2). Lowercase — стиль проекта (по образцу
/// <c>PrecategorizedCommodityStatusNames</c> в Domain и <c>CategorizationDecisionSources</c>
/// в Application).
/// </summary>
public static class BulkItemOutcomes
{
    /// <summary>Массовое подтверждение: категория предложения применена к товарам.</summary>
    public const string Applied = "applied";

    /// <summary>Массовое отклонение: предложение удалено без изменений в товарах (ADR-027, C4).</summary>
    public const string Declined = "declined";

    /// <summary>Строка пропущена по логическому конфликту; причина — в <see cref="BulkSkipReasons"/>.</summary>
    public const string Skipped = "skipped";

    /// <summary>
    /// Полное допустимое множество исходов ответа (ADR-036 §1.1/§1.2). Неизменяемое
    /// (<see cref="FrozenSet{T}"/>) с <see cref="StringComparer.Ordinal"/>: регистр и дефис значимы,
    /// контракт с фронтендом и endpoint-тестами фиксируется дословно.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new[] { Applied, Declined, Skipped }
        .ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>
/// Wire-имена причин пропуска строки массовой операции (ADR-036 §1.1/§1.2, C1): логический
/// конфликт — результат строки при HTTP <c>200</c>, а не ошибка запроса (<c>404</c>/<c>409</c>
/// на уровне батча не используются).
/// </summary>
public static class BulkSkipReasons
{
    /// <summary>Строка уже подтверждена параллельной операцией (снимок под блокировкой дал <c>applied</c>).</summary>
    public const string AlreadyApplied = "already-applied";

    /// <summary>Строки больше нет: её отклонили параллельной операцией до нашего снимка.</summary>
    public const string NotFound = "not-found";

    /// <summary>
    /// Полное допустимое множество причин пропуска. Неизменяемое (<see cref="FrozenSet{T}"/>)
    /// с <see cref="StringComparer.Ordinal"/>: дефис значим, сравнение точное.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new[] { AlreadyApplied, NotFound }
        .ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>
/// Результат по одной строке массового подтверждения (ADR-036 §1.1).
/// <para>
/// <see cref="Outcome"/> — одно из <see cref="BulkItemOutcomes"/>. У <c>applied</c>-строк
/// <see cref="CategoryId"/> и <see cref="CategoryName"/> заполнены фактически применённой
/// категорией из снимка строки (D1), а <see cref="Reason"/> = <see langword="null"/>.
/// У <c>skipped</c>-строк заполнен только <see cref="Reason"/> (<see cref="BulkSkipReasons"/>),
/// а <see cref="CategoryId"/>, <see cref="CategoryName"/> и <see cref="UpdatedCommodities"/>
/// не заполняются (<see langword="null"/>, сериализуются как <c>null</c>; клиент ориентируется на
/// <see cref="Outcome"/> — стиль проекта, прецедент <c>PrecategorizedCommodityDto.MerchantName</c>).
/// </para>
/// <para>
/// <see cref="UpdatedCommodities"/> — число фактически обновлённых товаров; <c>0</c> означает, что
/// решение всё равно состоялось (у всех товаров с этим именем категория уже была присвоена) —
/// D10 ADR-027.
/// </para>
/// </summary>
public sealed record BulkConfirmItemResult(
    Guid Id,
    string Outcome,
    string? Reason,
    int? CategoryId,
    string? CategoryName,
    int? UpdatedCommodities);

/// <summary>
/// Результат массового подтверждения страницы предложений (ADR-036 §1.1).
/// <para>
/// <see cref="Results"/> — по одной записи на каждый id запроса <b>в порядке запроса</b>
/// (сопоставление по id; порядок блокировок <c>(normalized_name, merchant_id, id)</c> на порядок
/// ответа не влияет). Инвариант ответа: <see cref="Applied"/> + <see cref="Skipped"/> =
/// <see cref="Requested"/> (число id в запросе).
/// </para>
/// <para>
/// <see cref="UpdatedCommodities"/> — сумма по <c>applied</c>-строкам; значение <c>0</c> не является
/// ошибкой (решение состоялось, D10 ADR-027). Отчёта о прогрессе здесь нет: результат формируется
/// один раз после commit (решение заказчика 5).
/// </para>
/// </summary>
public sealed record BulkConfirmResult(
    int Requested,
    int Applied,
    int Skipped,
    int UpdatedCommodities,
    IReadOnlyCollection<BulkConfirmItemResult> Results);

/// <summary>
/// Результат по одной строке массового отклонения (ADR-036 §1.2).
/// <para>
/// <see cref="Outcome"/> — <see cref="BulkItemOutcomes.Declined"/> либо
/// <see cref="BulkItemOutcomes.Skipped"/>; <see cref="Reason"/> заполняется только у
/// <c>skipped</c>-строк (<see cref="BulkSkipReasons"/>). Счётчика обновлённых товаров здесь нет:
/// отклонение не меняет <c>commodities</c> (асимметрия с подтверждением намеренная, §1.2 —
/// она повторяет асимметрию существующих поштучных контрактов).
/// </para>
/// </summary>
public sealed record BulkDeclineItemResult(
    Guid Id,
    string Outcome,
    string? Reason);

/// <summary>
/// Результат массового отклонения страницы предложений (ADR-036 §1.2).
/// <para>
/// <see cref="Results"/> — по одной записи на каждый id запроса <b>в порядке запроса</b>.
/// Инвариант ответа: <see cref="Declined"/> + <see cref="Skipped"/> = <see cref="Requested"/>
/// (число id в запросе).
/// </para>
/// <para>
/// Поля <c>updatedCommodities</c> нет: массовое отклонение товары не трогает, и отсутствие поля
/// в контракте — намеренная асимметрия с <see cref="BulkConfirmResult"/> (§1.2).
/// </para>
/// </summary>
public sealed record BulkDeclineResult(
    int Requested,
    int Declined,
    int Skipped,
    IReadOnlyCollection<BulkDeclineItemResult> Results);
