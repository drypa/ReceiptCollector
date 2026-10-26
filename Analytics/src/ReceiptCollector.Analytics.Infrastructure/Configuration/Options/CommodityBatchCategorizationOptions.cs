namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

/// <summary>
/// Настройки порционной категоризации страницы «Товары» (ADR-028 п. 3).
/// Секция конфигурации — "CommodityBatchCategorization"; переопределяется env-переменной
/// <c>CommodityBatchCategorization__SuggestTimeout</c> (стиль ADR-021).
/// </summary>
public sealed class CommodityBatchCategorizationOptions
{
    public const string SectionName = "CommodityBatchCategorization";

    /// <summary>
    /// Бюджет одного suggest-запроса страницы (по умолчанию 25 с). Независим от <c>AI:Timeout</c>
    /// (таймаут одного вызова провайдера): по истечении бюджета отменяются только вызовы ИИ,
    /// ответ отдаётся частичным (200), незавершённые названия — «не определена».
    /// Должен быть существенно меньше <c>proxy_read_timeout</c> nginx (120 с).
    /// </summary>
    public TimeSpan SuggestTimeout { get; set; } = TimeSpan.FromSeconds(25);
}
