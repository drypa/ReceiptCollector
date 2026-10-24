using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Потокобезопасный монитор активности (ADR-027 п.1, FR-1.4): хранит тики UTC в одном
/// <c>long</c> и обновляет их атомарно — без блокировок и I/O.
/// </summary>
internal sealed class ApiActivityMonitor : IApiActivityMonitor
{
    private readonly TimeProvider _timeProvider;
    private long _lastActivityTicks;

    public ApiActivityMonitor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // FR-1.3: если запросов не было с момента старта — отсчёт простоя ведётся от старта сервиса.
        _lastActivityTicks = _timeProvider.GetUtcNow().UtcTicks;
    }

    public DateTimeOffset LastActivityUtc => new(Volatile.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, _timeProvider.GetUtcNow().UtcTicks);
    }

    public bool IsIdle(TimeSpan threshold)
        => _timeProvider.GetUtcNow() - LastActivityUtc >= threshold;
}
