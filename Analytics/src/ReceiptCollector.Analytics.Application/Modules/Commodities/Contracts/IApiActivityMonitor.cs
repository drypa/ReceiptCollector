namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Глобальный монитор HTTP-активности Analytics (ADR-027 п.1, FR-1).
/// Используется фоновой предкатегоризацией для определения простоя.
/// Реализация — потокобезопасный Singleton (O(1) на обновление, без I/O и блокировок).
/// </summary>
public interface IApiActivityMonitor
{
    /// <summary>Отмечает текущий момент как последнюю активность (вызывается middleware).</summary>
    void Touch();

    /// <summary>Момент последней активности (UTC); при отсутствии запросов — время старта сервиса.</summary>
    DateTimeOffset LastActivityUtc { get; }

    /// <summary>Простой: с последней активности прошло не меньше <paramref name="threshold"/>.</summary>
    bool IsIdle(TimeSpan threshold);
}
