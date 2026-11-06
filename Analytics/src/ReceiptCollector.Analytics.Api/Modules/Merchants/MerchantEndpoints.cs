using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Merchants;

public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/merchants");
        group.WithTags("Merchants");

        group.MapGet("", GetAll);
        group.MapPut("/{merchantId:guid}/category", UpdateCategory);
        group.MapPut("/{merchantId:guid}/name", UpdateMerchantName);
        group.MapGet("/categories", ListCategories);

        // Товарная категория по умолчанию магазина (ADR-032, п. 6): настройка/сброс и дожим.
        group.MapPut("/{merchantId:guid}/default-commodity-category", UpdateDefaultCommodityCategory);
        group.MapPost("/{merchantId:guid}/default-commodity-category/apply", ApplyDefaultCommodityCategory);

        return app;
    }

    public static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid();
        }

        if (limit <= 0)
        {
            return Results.BadRequest("limit must be greater than zero.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var merchants = await merchantRepository.GetAllAsync(cancellationToken);

        IEnumerable<Merchant> filtered = merchants;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = filtered.Count();
        var page = filtered
            .OrderBy(m => m.Name)
            .Skip(offset)
            .Take(limit)
            .Select(ToDto)
            .ToList();

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(page);
    }

    public static async Task<IResult> UpdateCategory(
        Guid merchantId,
        [FromBody] UpdateMerchantCategoryRequest request,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid();
        }

        var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound("Merchant not found.");
        }

        if (!Enum.IsDefined(typeof(MerchantCategory), request.CategoryId))
        {
            return Results.BadRequest("Invalid category.");
        }

        var category = (MerchantCategory)request.CategoryId;
        await merchantRepository.UpdateCategoryAsync(merchantId, category, cancellationToken);

        return Results.Ok(new { categoryId = request.CategoryId, categoryName = MerchantCategoryHelper.GetDisplayName(category) });
    }

    public static async Task<IResult> ListCategories(
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid();
        }

        var categories = MerchantCategoryHelper.GetAll()
            .Select(c => new MerchantCategoryDto((int)c.Id, c.Name))
            .ToList();

        return Results.Ok(categories);
    }

    public static async Task<IResult> UpdateMerchantName(Guid merchantId, [FromBody] UpdateMerchantNameRequest request,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        // Проверяем, что пользователь авторизован
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        // Проверяем, что пользователь является администратором
        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid();
        }

        // Получаем магазин по идентификатору
        var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound("Merchant not found.");
        }

        // Нормализуем и валидируем имя магазина
        var normalized = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Results.BadRequest("Merchant name is required.");
        }

        if (normalized.Length > 256)
        {
            return Results.BadRequest("Merchant name must be at most 256 characters.");
        }

        // Обновляем имя магазина
        merchant.UpdateName(normalized);

        // Сохраняем обновленный магазин
        await merchantRepository.AddAsync(merchant, cancellationToken);

        return Results.Ok("Merchant name updated successfully.");
    }

    /// <summary>
    /// Установить или сбросить товарную категорию по умолчанию магазина (ADR-032, п. 6; FR-M1).
    /// Тело <c>{ "categoryId": int | null }</c>; <c>null</c> = сброс (и отсутствующее поле тоже);
    /// JSON-literal <c>null</c> вместо объекта — <c>400</c> (guard ниже), а не NRE/500. Порядок проверок:
    /// 401 → 403 (не-админ) → 404 (магазин не найден) → тело/валидация кода справочника
    /// (<c>Enum.IsDefined</c> и <c>!= Undefined</c>, A1; инварианта на enum в домене нет —
    /// валидация здесь, прецедент ADR-027 D11) → сохранение существующим repo-путём редактирования
    /// merchant (ADR 011: GetByIdAsync → mutate → AddAsync, round-trip колонки обеспечен маппингом T-02).
    /// Хранится отдельно от категории самого магазина (<c>MerchantCategory</c>) — FR-M1.
    /// 403 отдаётся явно (<see cref="Results.StatusCode(int)"/>) по уточнению ADR-027 —
    /// дефект <c>Results.Forbid()</c> (500 без схемы аутентификации) в новых хендлах не воспроизводим.
    /// </summary>
    public static async Task<IResult> UpdateDefaultCommodityCategory(
        Guid merchantId,
        [FromBody] UpdateMerchantDefaultCategoryRequest request,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        // 401 (нет пользователя) → 403 (не-админ) — общий источник проверок (ADR-028 п. 10).
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound("Merchant not found.");
        }

        // JSON-literal null десериализуется в null-ссылку (не в «сброс»): 400 вместо NRE/500.
        // Порядок сохранён: права и 404 — раньше валидации тела.
        if (request is null)
        {
            return Results.BadRequest("Request body is required.");
        }

        // null — сброс (значение допустимо без валидации кода); иначе код должен существовать
        // в справочнике и не быть Undefined(0) — CHECK «<> 0» на колонке дублирует на уровне БД.
        if (request.CategoryId is int categoryId)
        {
            if (!Enum.IsDefined(typeof(CommodityCategory), categoryId)
                || categoryId == (int)CommodityCategory.Undefined)
            {
                return Results.BadRequest("Invalid category.");
            }

            merchant.UpdateDefaultCommodityCategory((CommodityCategory)categoryId);
        }
        else
        {
            merchant.UpdateDefaultCommodityCategory(null);
        }

        await merchantRepository.AddAsync(merchant, cancellationToken);

        // Ответ — фактическое сохранённое состояние (сброс → { categoryId: null, categoryName: null }).
        var saved = merchant.DefaultCommodityCategory;
        return Results.Ok(new UpdateMerchantDefaultCategoryResponse(
            saved is null ? null : (int)saved,
            saved is null ? null : CommodityCategoryHelper.GetDisplayName(saved.Value)));
    }

    /// <summary>
    /// Дожим исторических позиций магазина категорией по умолчанию (ADR-032, п. 4/6; FR-M3) —
    /// операция кнопки «Категоризировать существующие». Порядок прав — как у PUT
    /// (<c>AdminAuthorization</c>: 401 → явный 403), затем сервис
    /// <see cref="IMerchantDefaultCategoryService.ApplyExistingAsync"/> (админ-инициатор — для аудита;
    /// позиции обрабатываются всех пользователей, E1). Маппинг исходов: NotFound → 404;
    /// DefaultNotConfigured → 400 текстом; Applied → 200 <c>{ updated }</c>, включая
    /// <c>updated = 0</c> (идемпотентность, повторное нажатие кнопки — успех).
    /// </summary>
    public static async Task<IResult> ApplyDefaultCommodityCategory(
        Guid merchantId,
        [FromServices] IMerchantDefaultCategoryService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // После успешной авторизации UserId гарантированно непуст (иначе был бы 401).
        var adminUserId = UserContext.UserId!.Value;
        var result = await service.ApplyExistingAsync(adminUserId, merchantId, cancellationToken);

        return result.Outcome switch
        {
            MerchantDefaultApplyOutcome.NotFound => Results.NotFound("Merchant not found."),
            MerchantDefaultApplyOutcome.DefaultNotConfigured =>
                Results.BadRequest("Default commodity category is not set for this merchant."),
            _ => Results.Ok(new ApplyMerchantDefaultCategoryResponse(result.UpdatedCount)),
        };
    }

    private static MerchantDto ToDto(Merchant merchant)
    {
        var defaultCategory = merchant.DefaultCommodityCategory;
        return new MerchantDto(
            merchant.Id,
            merchant.Name,
            (int)merchant.Category,
            merchant.Address,
            merchant.Inn,
            defaultCategory is null ? null : (int)defaultCategory,
            defaultCategory is null ? null : CommodityCategoryHelper.GetDisplayName(defaultCategory.Value));
    }
}

public sealed record UpdateMerchantCategoryRequest(int CategoryId);

public sealed record UpdateMerchantNameRequest(string Name);

/// <summary>Тело PUT default-commodity-category (ADR-032, п. 6): <c>null</c> = сброс дефолта.</summary>
public sealed record UpdateMerchantDefaultCategoryRequest(int? CategoryId);

/// <summary>Ответ PUT: сохранённая пара код/имя; при сбросе оба <c>null</c> (FR-M1).</summary>
public sealed record UpdateMerchantDefaultCategoryResponse(int? CategoryId, string? CategoryName);

/// <summary>Ответ apply: число позиций, получивших категорию; 0 — все уже категоризированы.</summary>
public sealed record ApplyMerchantDefaultCategoryResponse(int Updated);
