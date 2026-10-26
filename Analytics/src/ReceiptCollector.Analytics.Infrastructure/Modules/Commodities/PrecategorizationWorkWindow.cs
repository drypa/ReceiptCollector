using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Рабочее окно фоновой предкатегоризации (ADR-030): сервис проверяет простой и запускает
/// сессии только внутри окна. Время суток трактуется в часовом поясе
/// <see cref="PrecategorizationOptions.WorkWindowTimeZone"/>; пустое значение — локальное
/// время сервера. Окно не задано — поведение прежнее (круглосуточно).
/// </summary>
internal static class PrecategorizationWorkWindow
{
    /// <summary>Заданы ли обе границы окна (валидация не допускает только одну).</summary>
    public static bool IsConfigured(PrecategorizationOptions options)
        => options.WorkWindowStart.HasValue && options.WorkWindowEnd.HasValue;

    /// <summary>
    /// Открыто ли окно в момент <paramref name="now"/> (уже в часовом поясе окна).
    /// Границы полуинтервала: начало включается, конец — нет. Окно через полночь
    /// (начало больше конца) поддерживается; нулевое окно (начало = конец) закрыто всегда.
    /// </summary>
    public static bool IsOpen(PrecategorizationOptions options, DateTimeOffset now)
    {
        if (options.WorkWindowStart is not { } start || options.WorkWindowEnd is not { } end)
        {
            return true;
        }

        if (start == end)
        {
            return false;
        }

        var timeOfDay = now.TimeOfDay;
        return start < end
            ? timeOfDay >= start && timeOfDay < end
            : timeOfDay >= start || timeOfDay < end;
    }

    /// <summary>Человекочитаемое описание окна для стартового лога.</summary>
    public static string Describe(PrecategorizationOptions options)
    {
        if (!IsConfigured(options))
        {
            return "not configured (around the clock)";
        }

        var start = options.WorkWindowStart!.Value;
        var end = options.WorkWindowEnd!.Value;
        var zone = string.IsNullOrWhiteSpace(options.WorkWindowTimeZone)
            ? "server local time"
            : options.WorkWindowTimeZone;

        return $"{start:c}–{end:c} ({zone})";
    }
}
