using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Сценарий «Автоматическая категоризация товаров чека» (UC-5, ADR 009).
/// Приоритет: категория из чека → in-memory кэш категорий (ADR 019) → AI (решение C2).
/// Suggest-режим ничего не сохраняет: обновление категорий выполняется только
/// через подтверждение пользователем (PUT /api/receipts/{id}/categories).
/// </summary>
public interface ICommodityCategorizationService
{
    /// <summary>
    /// Предлагает категории для всех товаров чека пользователя. Ничего не записывает в БД.
    /// </summary>
    /// <param name="userId">Владелец чека (кэш категорий — сквозной, но чек и товары принадлежат пользователю).</param>
    /// <param name="receiptId">Чек, товары которого нужно категоризировать.</param>
    Task<ReceiptCategorizationResult> SuggestForReceiptAsync(
        Guid userId,
        Guid receiptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждает (сохраняет) категории товаров чека. Чек должен принадлежать пользователю.
    /// Пустая категория (null) сбрасывает категорию у товаров без категории.
    /// Категория Undefined (0) запрещена к явному присвоению — ArgumentException.
    /// Категория сохраняется в сквозной in-memory кэш (ADR 019, без userId).
    /// Возвращает количество обработанных товаров.
    /// <para>
    /// По присвоениям с валидным клиентским снимком предложения (ADR-033 п. 3) после сохранения
    /// пишется best-effort событие журнала «предложено → принято». Сбой журнала не влияет на
    /// сохранение и возвращаемый результат (NFR-3).
    /// </para>
    /// </summary>
    /// <param name="userId">Владелец чека (он же принявший по журналу).</param>
    /// <param name="receiptId">Чек, товары которого подтверждаются.</param>
    /// <param name="items">Список подтверждённых присвоений {commodityId, category, снимок предложения}.</param>
    /// <param name="acceptedByAdmin">
    /// Снимок <c>users.is_admin</c> на момент решения; резолвит эндпоинт (ADR-033 п. 3.5).
    /// </param>
    Task<int> ApplyConfirmedCategoriesAsync(
        Guid userId,
        Guid receiptId,
        IReadOnlyCollection<ConfirmedCategoryAssignment> items,
        bool acceptedByAdmin,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Подтверждённое пользователем присвоение категории товару (category может быть null = сброс).
/// <para>
/// <see cref="SuggestedCategory"/>/<see cref="SuggestionSource"/> — аддитивный клиентский снимок
/// предложения (ADR-033 п. 3, решение A1): заполнены только если клиент прислал валидную пару
/// «категория + источник». Без снимка событие журнала не пишется (FR-5b). Снимок не влияет на
/// применение категории (NFR-1).
/// </para>
/// </summary>
public sealed record ConfirmedCategoryAssignment(
    Guid CommodityId,
    CommodityCategory? Category,
    CommodityCategory? SuggestedCategory = null,
    string? SuggestionSource = null);