using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Modules.Receipts;

/// <summary>
/// Эндпоинты сценария «Автоматическая категоризация товаров чека» (ADR 009).
/// POST /api/receipts/{id}/categories/suggest — предложить категории (ничего не сохраняет).
/// PUT /api/receipts/{id}/categories — подтвердить и сохранить категории товаров чека.
/// </summary>
public static class ReceiptCategorizationEndpoints
{
    public static IEndpointRouteBuilder MapReceiptCategorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts");
        group.WithTags("Receipts");

        group.MapPost("/{receiptId:guid}/categories/suggest", Suggest);
        group.MapPut("/{receiptId:guid}/categories", Save);

        return app;
    }

    public static async Task<IResult> Suggest(
        Guid receiptId,
        [FromServices] ICommodityCategorizationService service,
        [FromServices] IOptions<AiOptions> aiOptions,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(aiOptions.Value.BaseUrl))
        {
            return Results.Json(
                new { error = "AI categorization is not configured (AI:BaseUrl is empty)." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await service.SuggestForReceiptAsync(userId.Value, receiptId, cancellationToken);
            return Results.Ok(result);
        }
        catch (ReceiptNotFoundException)
        {
            return Results.NotFound("Receipt not found.");
        }
    }

    /// <summary>
    /// Подтверждение категорий чека. Аддитивно принимает клиентский снимок предложения
    /// (<c>suggestedCategory</c>/<c>suggestionSource</c>, ADR-033 п. 3): валидный снимок даёт событие
    /// журнала, невалидный — игнорируется без ошибки запроса (NFR-2). Существующая валидация
    /// <c>items</c>/<c>category</c> и все коды ответов не меняются.
    /// </summary>
    public static async Task<IResult> Save(
        Guid receiptId,
        [FromBody] SaveCategoriesRequest request,
        [FromServices] ICommodityCategorizationService service,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("items must not be empty.");
        }

        var assignments = new List<ConfirmedCategoryAssignment>(request.Items.Count);
        var invalidSnapshots = 0;

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];

            if (item.CommodityId == Guid.Empty)
            {
                return Results.BadRequest($"items[{index}].commodityId is not a valid GUID.");
            }

            var snapshot = ParseSuggestionSnapshot(
                item.SuggestedCategory,
                item.SuggestionSource,
                out var suggestedCategory,
                out var suggestionSource);

            if (snapshot == SuggestionSnapshotKind.Invalid)
            {
                invalidSnapshots++;
            }

            CommodityCategory? category = null;

            if (!string.IsNullOrWhiteSpace(item.Category))
            {
                if (int.TryParse(item.Category, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return Results.BadRequest($"items[{index}].category must be a category name, not a number.");
                }

                if (!Enum.TryParse<CommodityCategory>(item.Category, ignoreCase: true, out var parsedCategory)
                    || !Enum.IsDefined(parsedCategory)
                    || parsedCategory == CommodityCategory.Undefined)
                {
                    return Results.BadRequest($"items[{index}].category '{item.Category}' is not a valid category name.");
                }

                category = parsedCategory;
            }

            // Пустая строка/пробелы трактуются как сброс категории (null).
            assignments.Add(new ConfirmedCategoryAssignment(
                item.CommodityId,
                category,
                snapshot == SuggestionSnapshotKind.Valid ? suggestedCategory : null,
                snapshot == SuggestionSnapshotKind.Valid ? suggestionSource : null));
        }

        if (invalidSnapshots > 0)
        {
            // ADR-033 п. 3.2/п. 9: невалидный снимок — не ошибка запроса; в Warning только количество.
            loggerFactory
                .CreateLogger("ReceiptCategorizationEndpoints")
                .LogWarning(
                    "Receipt categories save: {InvalidSnapshotCount} category suggestion snapshots were ignored as invalid.",
                    invalidSnapshots);
        }

        var acceptedByAdmin = false;
        if (assignments.Any(assignment => assignment.SuggestedCategory.HasValue))
        {
            // ADR-033 п. 3.5: снимок users.is_admin резолвится один раз и только при наличии
            // хотя бы одного валидного снимка предложения.
            var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
            acceptedByAdmin = user?.IsAdmin ?? false;
        }

        try
        {
            var updated = await service.ApplyConfirmedCategoriesAsync(
                userId.Value, receiptId, assignments, acceptedByAdmin, cancellationToken);
            return Results.Ok(new SaveCategoriesResponse(receiptId, updated));
        }
        catch (ReceiptNotFoundException)
        {
            return Results.NotFound("Receipt not found.");
        }
    }

    /// <summary>Состояние клиентского снимка предложения в элементе сохранения (ADR-033 п. 3).</summary>
    private enum SuggestionSnapshotKind
    {
        /// <summary>Оба поля пусты — снимка нет (FR-5b).</summary>
        Absent,

        /// <summary>Снимок валиден: предложенная категория и источник допустимы.</summary>
        Valid,

        /// <summary>Снимок прислан частично/невалидно — игнорируется без ошибки запроса.</summary>
        Invalid
    }

    /// <summary>
    /// Разбор снимка предложения (ADR-033 п. 3.1–3.2): оба поля пусты — снимка нет; иначе снимок
    /// валиден только при непустой, не числовой, определённой и не <c>Undefined</c> категории и
    /// источнике из четырёх допустимых wire-имён (ADR-033 п. 6). Валидацию формы события всё равно
    /// повторяет recorder — здесь отбрасывается только заведомо невалидный клиентский снимок.
    /// </summary>
    private static SuggestionSnapshotKind ParseSuggestionSnapshot(
        string? suggestedCategory,
        string? suggestionSource,
        out CommodityCategory category,
        out string source)
    {
        category = CommodityCategory.Undefined;
        source = string.Empty;

        if (string.IsNullOrWhiteSpace(suggestedCategory) && string.IsNullOrWhiteSpace(suggestionSource))
        {
            return SuggestionSnapshotKind.Absent;
        }

        if (string.IsNullOrWhiteSpace(suggestedCategory)
            || string.IsNullOrWhiteSpace(suggestionSource)
            || int.TryParse(suggestedCategory, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return SuggestionSnapshotKind.Invalid;
        }

        if (!Enum.TryParse(suggestedCategory, ignoreCase: true, out category)
            || !Enum.IsDefined(category)
            || category == CommodityCategory.Undefined
            || !CategorizationDecisionSources.IsAllowed(suggestionSource))
        {
            category = CommodityCategory.Undefined;
            return SuggestionSnapshotKind.Invalid;
        }

        source = suggestionSource;
        return SuggestionSnapshotKind.Valid;
    }
}

/// <summary>
/// Тело <c>PUT /api/receipts/{receiptId}/categories</c>.
/// <para>
/// <see cref="SaveCategoryItemRequest.SuggestedCategory"/> и
/// <see cref="SaveCategoryItemRequest.SuggestionSource"/> — аддитивные опциональные поля снимка
/// предложения (ADR-033 п. 3, решение A1): без них контракт и поведение прежние.
/// </para>
/// </summary>
public sealed record SaveCategoriesRequest(IReadOnlyList<SaveCategoryItemRequest>? Items);

/// <summary>
/// Один элемент сохранения: <c>category</c> — имя enum (пусто = сброс в «Не указана»);
/// <c>suggestedCategory</c>/<c>suggestionSource</c> — опциональный снимок предложения.
/// </summary>
public sealed record SaveCategoryItemRequest(
    Guid CommodityId,
    string? Category,
    string? SuggestedCategory = null,
    string? SuggestionSource = null);

public sealed record SaveCategoriesResponse(Guid ReceiptId, int Updated);