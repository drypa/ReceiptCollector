using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Commodities;

/// <summary>
/// Порционная категоризация страницы «Товары» (ADR-028 п. 1.1/1.2):
/// <list type="bullet">
///   <item><c>POST /api/commodities/categories/suggest</c> — предложения по порции (read-only);</item>
///   <item><c>PUT /api/commodities/categories</c> — пакетное assign-only сохранение.</item>
/// </list>
/// Права: нет пользователя → <c>401</c>, не админ → <c>403</c> явным <c>Results.StatusCode</c>
/// (ADR-028 п. 10, ADR-027 п. 7). Стиль ошибок — текстовые тела, без ProblemDetails.
/// </summary>
public static class CommodityBatchCategorizationEndpoints
{
    /// <summary>Максимум идентификаторов в одном suggest-запросе (ADR-028 п. 1.1).</summary>
    private const int MaxCommodityIds = 200;

    public static IEndpointRouteBuilder MapCommodityBatchCategorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commodities");
        group.WithTags("Commodities");

        group.MapPost("/categories/suggest", Suggest);
        group.MapPut("/categories", Save);

        return app;
    }

    /// <summary>
    /// Предложения категорий по порции товаров текущего пользователя (ADR-028 п. 1.1).
    /// Неизвестные/чужие id не дают ошибку — просто отсутствуют в <c>items</c>.
    /// Пустой <c>AI:BaseUrl</c> — <c>200</c> с <c>aiAvailable = false</c> (не <c>503</c>, C7).
    /// </summary>
    public static async Task<IResult> Suggest(
        [FromBody] SuggestCommodityCategoriesRequest request,
        [FromServices] ICommodityBatchCategorizationService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // Порядок проверок ADR-028 п. 1.1: 401 → 403 → валидация → сервис.
        var commodityIds = request.CommodityIds;
        if (commodityIds is null || commodityIds.Count == 0)
        {
            return Results.BadRequest("commodityIds must not be empty.");
        }

        if (commodityIds.Count > MaxCommodityIds)
        {
            return Results.BadRequest($"commodityIds must contain at most {MaxCommodityIds} items.");
        }

        var seen = new HashSet<Guid>(commodityIds.Count);
        for (var index = 0; index < commodityIds.Count; index++)
        {
            var commodityId = commodityIds[index];

            if (commodityId == Guid.Empty)
            {
                return Results.BadRequest($"commodityIds[{index}] is not a valid GUID.");
            }

            if (!seen.Add(commodityId))
            {
                return Results.BadRequest($"commodityIds contains duplicate id '{commodityId}'.");
            }
        }

        try
        {
            var result = await service.SuggestAsync(
                UserContext.UserId!.Value,
                commodityIds,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            // > 20 уникальных названий в порции (ADR-028 п. 1.1).
            return Results.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Пакетное сохранение категорий (ADR-028 п. 1.2): assign-only, пустая категория = «не трогаем»,
    /// <c>Undefined</c> и числа запрещены. Если после фильтрации «не трогаем» менять нечего —
    /// <c>200 { updated = 0 }</c> без обращения к сервису. Устаревшая страница → <c>409</c>
    /// (ничего не записано).
    /// </summary>
    public static async Task<IResult> Save(
        [FromBody] SaveCommodityCategoriesRequest request,
        [FromServices] ICommodityBatchCategorizationService service,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // Статический класс нельзя использовать как аргумент ILogger<T> — берём категорию по имени.
        var logger = loggerFactory.CreateLogger("CommodityBatchCategorizationEndpoints");

        var items = request.Items;
        if (items is null || items.Count == 0)
        {
            return Results.BadRequest("items must not be empty.");
        }

        var changes = new List<CommodityCategoryChange>(items.Count);
        var seen = new HashSet<Guid>(items.Count);
        var skipped = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            if (item.CommodityId == Guid.Empty)
            {
                return Results.BadRequest($"items[{index}].commodityId is not a valid GUID.");
            }

            if (!seen.Add(item.CommodityId))
            {
                return Results.BadRequest($"items contains duplicate commodityId '{item.CommodityId}'.");
            }

            if (string.IsNullOrWhiteSpace(item.Category))
            {
                // C4: пустое значение/пробелы — «не трогаем», а не сброс категории.
                skipped++;
                continue;
            }

            if (int.TryParse(item.Category, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return Results.BadRequest($"items[{index}].category must be a category name, not a number.");
            }

            // Парсинг зеркально PUT /api/receipts/{id}/categories (регистр принимается).
            if (!Enum.TryParse<CommodityCategory>(item.Category, ignoreCase: true, out var category)
                || !Enum.IsDefined(category)
                || category == CommodityCategory.Undefined)
            {
                return Results.BadRequest($"items[{index}].category '{item.Category}' is not a valid category name.");
            }

            changes.Add(new CommodityCategoryChange(item.CommodityId, category));
        }

        if (changes.Count == 0)
        {
            // «Не трогаем» все элементы — без записи в БД и без вызова сервиса.
            return Results.Ok(new SaveCommodityCategoriesResponse(0));
        }

        try
        {
            var updated = await service.ApplyAsync(
                UserContext.UserId!.Value,
                changes,
                cancellationToken);

            // ADR-028 п. 11: Information — updated и число пропущенных «не трогаем».
            logger.LogInformation(
                "Commodity batch save endpoint applied: {UpdatedCount} rows changed, {SkippedCount} items skipped as 'do not touch'.",
                updated,
                skipped);

            return Results.Ok(new SaveCommodityCategoriesResponse(updated));
        }
        catch (CommoditiesNotAccessibleException)
        {
            // Устаревшая страница: ничего не записано, черновик на клиенте сохраняется.
            logger.LogWarning(
                "Commodity batch save endpoint rejected a stale page: {RequestedCount} items, {SkippedCount} skipped.",
                changes.Count,
                skipped);

            return Results.Conflict(
                "Some rows are no longer accessible. Refresh the page and retry saving.");
        }
    }
}

/// <summary>Тело <c>POST /api/commodities/categories/suggest</c>.</summary>
public sealed record SuggestCommodityCategoriesRequest(IReadOnlyList<Guid>? CommodityIds);

/// <summary>Тело <c>PUT /api/commodities/categories</c>.</summary>
public sealed record SaveCommodityCategoriesRequest(IReadOnlyList<SaveCommodityCategoryItemRequest>? Items);

/// <summary>Один элемент сохранения: <c>category</c> — имя enum; <c>null</c>/пробелы — «не трогаем».</summary>
public sealed record SaveCommodityCategoryItemRequest(Guid CommodityId, string? Category);

/// <summary>Ответ сохранения: число строк, у которых значение реально изменилось (C9).</summary>
public sealed record SaveCommodityCategoriesResponse(int Updated);
