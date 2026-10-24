using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

namespace ReceiptCollector.Analytics.Api.Middleware;

/// <summary>
/// Отмечает HTTP-активность для детектирования простоя фоновой предкатегоризации
/// (ADR-027 п.1, FR-1.1–1.4). Регистрируется ДО <c>UseStaticFiles()</c>: активностью
/// считаются и статика, и загрузка SPA, и API-запросы (D3).
/// </summary>
public sealed class RequestActivityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiActivityMonitor _monitor;

    public RequestActivityMiddleware(RequestDelegate next, IApiActivityMonitor monitor)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsExcludedPath(context.Request.Path))
        {
            _monitor.Touch();
        }

        return _next(context);
    }

    /// <summary>
    /// Расширяемый предикат исключений (FR-1.2, D3): health-пробник мониторинга не должен
    /// сбрасывать таймер простоя, иначе фон не запустится никогда. Новые пробники
    /// добавляются здесь же, без переписывания логики (риск 8 ADR-027).
    /// </summary>
    private static bool IsExcludedPath(PathString path)
        => path.StartsWithSegments("/health");
}
