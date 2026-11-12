using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.CommodityCategoryKeywords;

/// <summary>
/// Админский API слов категоризации (ADR-035 п. 7, FR-3/FR-4/FR-10): полный снимок категорий
/// со словами и адресные добавление/удаление одного слова (решение G1). Все три эндпоинта —
/// под <see cref="AdminAuthorization"/> (не админ → 403, нет сессии → 401); текстовые тела
/// ошибок без ProblemDetails, коды и тексты — символ в символ таблице ADR-035 п. 7
/// (стиль <c>PrecategorizedCommodityEndpoints</c>/<c>CategorizationDecisionEndpoints</c>).
/// </summary>
public static class CommodityCategoryKeywordEndpoints
{
    public static IEndpointRouteBuilder MapCommodityCategoryKeywordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commodity-category-keywords");
        group.WithTags("CommodityCategoryKeywords");

        group.MapGet("", GetAll);
        group.MapPost("", Add);
        group.MapDelete("/{id:guid}", Delete);

        return app;
    }

    /// <summary>
    /// <c>GET /api/commodity-category-keywords</c> → <c>200</c>: все категории (кроме
    /// <c>Undefined</c>), включая пустые списки; <c>X-Total-Count</c> и пагинация не вводятся
    /// (объём ограничен, ADR-035 п. 7).
    /// </summary>
    public static async Task<IResult> GetAll(
        [FromServices] ICommodityCategoryKeywordService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        var categories = await service.GetAllAsync(cancellationToken);
        return Results.Ok(categories);
    }

    /// <summary>
    /// <c>POST /api/commodity-category-keywords</c>: <c>200</c> с созданным элементом
    /// (не <c>201</c> — сознательно: нет GET-by-id, все мутации проекта отвечают Ok/NoContent,
    /// ADR-035 п. 7), <c>400</c>/<c>409</c> с текстами из таблицы ADR-035 п. 7.
    /// Порядок проверок — порядок той же таблицы: категория → <c>listType</c> → слово →
    /// дубликат/лимит (последние два возвращает сервис).
    /// </summary>
    public static async Task<IResult> Add(
        [FromBody] AddCommodityCategoryKeywordRequest request,
        [FromServices] ICommodityCategoryKeywordService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        if (request.CategoryId == (int)CommodityCategory.Undefined
            || !Enum.IsDefined(typeof(CommodityCategory), request.CategoryId))
        {
            return Results.BadRequest("Invalid category.");
        }

        if (!KeywordListTypeNames.TryParse(request.ListType, out _))
        {
            return Results.BadRequest("listType must be 'boost' or 'exclude'.");
        }

        // Слово: те же границы, что у сервиса, плюс проверка нормализованной формы —
        // страховка на случай смены нормализации (например, full case folding: «İ» U+0130
        // даёт 2 символа) или сужения колонки varchar(64); на текущем рантайме .NET
        // ToLowerInvariant расширение не наблюдается. Такой случай отклоняется тем же 400
        // (ADR-035 п. 7), не доводя до вставки с ошибкой 22001; длина — в кодовых точках,
        // как char_length в PostgreSQL.
        if (!CommodityCategoryKeywordRules.IsValidWord(request.Word)
            || CommodityCategoryKeywordRules.NormalizeWord(request.Word!)
                    .EnumerateRunes().Count() > CommodityCategoryKeywordRules.MaxWordLength)
        {
            return Results.BadRequest(
                $"word must be between {CommodityCategoryKeywordRules.MinWordLength} and "
                + $"{CommodityCategoryKeywordRules.MaxWordLength} characters after trimming.");
        }

        var result = await service.AddAsync(
            request.CategoryId,
            request.ListType,
            request.Word,
            cancellationToken);

        return result.Outcome switch
        {
            AddKeywordOutcome.Duplicate => Results.Conflict("word already exists in this list."),
            AddKeywordOutcome.LimitReached => Results.Conflict(
                $"list limit of {CommodityCategoryKeywordRules.MaxWordsPerList} words reached."),
            AddKeywordOutcome.Added => Results.Ok(new AddCommodityCategoryKeywordResponse(
                result.Id!.Value,
                result.CategoryId!.Value,
                result.ListType!,
                result.Word!)),
            // Новый исход enum не должен молча превращаться в NRE в ветке «по умолчанию».
            _ => throw new InvalidOperationException(
                $"Unknown add keyword outcome: {result.Outcome}.")
        };
    }

    /// <summary>
    /// <c>DELETE /api/commodity-category-keywords/{id:guid}</c>: <c>204</c> — удалено;
    /// <c>404</c> — строки нет (в т.ч. удалена в другой вкладке). Ретроактивного пересчёта
    /// уже проставленных категорий нет (ADR-035, «вне области»).
    /// </summary>
    public static async Task<IResult> Delete(
        Guid id,
        [FromServices] ICommodityCategoryKeywordService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        var outcome = await service.DeleteAsync(id, cancellationToken);

        return outcome switch
        {
            DeleteKeywordOutcome.NotFound => Results.NotFound("word not found."),
            _ => Results.NoContent()
        };
    }
}

/// <summary>
/// Тело <c>POST /api/commodity-category-keywords</c> (ADR-035 п. 7): адресная операция
/// «одно слово в один список одной категории». <c>word</c> сохраняется после trim в
/// исходном регистре (FR-5).
/// </summary>
public sealed record AddCommodityCategoryKeywordRequest(int CategoryId, string? ListType, string? Word);

/// <summary>
/// Ответ <c>200</c> на <c>POST</c> — созданный элемент (ADR-035 п. 7): <c>id</c> для адресного
/// <c>DELETE</c>, слово после trim в исходном регистре, <c>listType</c> — wire-имя.
/// </summary>
public sealed record AddCommodityCategoryKeywordResponse(Guid Id, int CategoryId, string ListType, string Word);
