namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

/// <summary>
/// Настройки фоновой предкатегоризации (ADR-027 п.9, NFR-7).
/// Секция конфигурации — "Precategorization"; переопределяется env-переменными
/// <c>Precategorization__*</c> без пересборки (стиль AI__*, ADR-021).
/// <c>Enabled = false</c> отключает ТОЛЬКО фоновый сервис: админский раздел и поиск
/// по очереди в ручном suggest продолжают работать на накопленных строках (FR-1.5, FR-9.7).
/// </summary>
public sealed class PrecategorizationOptions
{
    public const string SectionName = "Precategorization";

    /// <summary>Включает фоновый сервис предкатегоризации.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Простой, после которого запускается сессия (по умолчанию 5 минут).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Период проверки простоя (по умолчанию 30 секунд).</summary>
    public TimeSpan IdleCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Максимум позиций за сессию (по умолчанию 5).</summary>
    public int BatchLimit { get; set; } = 5;

    /// <summary>Пауза между вызовами ИИ (по умолчанию 2 секунды).</summary>
    public TimeSpan IntervalBetweenCalls { get; set; } = TimeSpan.FromSeconds(2);
}
