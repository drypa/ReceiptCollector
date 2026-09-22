namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

/// <summary>
/// Настройки in-memory кэша категорий товаров (ADR 019).
/// Секция "CommodityCategoryCache"; env-переменная CommodityCategoryCache__MaxSize (без пересборки, стиль AI__*).
/// MaxSize < 1 — кэш отключён.
/// </summary>
public sealed class CommodityCategoryCacheOptions
{
    public const string SectionName = "CommodityCategoryCache";
    public int MaxSize { get; set; } = 1000;
}