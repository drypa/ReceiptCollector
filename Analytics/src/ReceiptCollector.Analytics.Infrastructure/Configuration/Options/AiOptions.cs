namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

/// <summary>
/// Настройки AI-категоризации товаров (ADR 009, решение C6).
/// Секция конфигурации — "AI" (переопределяется env-переменной AI__BaseUrl и т.п.).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>
    /// Базовый URL OpenAI-совместимого API (например, "https://api.openai.com/v1").
    /// Клиент обращается к "{BaseUrl}/chat/completions".
    /// Пустое значение = AI-категоризация отключена (эндпоинт suggest отвечает 503).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Имя модели (например, "qwen").</summary>
    public string Model { get; set; } = "qwen";

    /// <summary>Таймаут одного запроса к AI.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Максимальное количество одновременных запросов к AI при категоризации одного чека.</summary>
    public int Concurrency { get; set; } = 3;

    /// <summary>API-ключ (Bearer). Пустое значение — запросы без авторизации.</summary>
    public string? ApiKey { get; set; }
}