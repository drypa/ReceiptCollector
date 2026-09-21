using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Сценарий «Автоматическая категоризация товаров чека» (UC-5, ADR 009).
/// Приоритет: категория из чека → кэш ранее присвоенных категорий → AI (решение C2).
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
    /// Категория сохраняется в сквозной кэш commodity_category_assignments (без userId).
    /// Возвращает количество обработанных товаров.
    /// </summary>
    /// <param name="userId">Владелец чека.</param>
    /// <param name="receiptId">Чек, товары которого подтверждаются.</param>
    /// <param name="items">Список подтверждённых присвоений {commodityId, category}.</param>
    Task<int> ApplyConfirmedCategoriesAsync(
        Guid userId,
        Guid receiptId,
        IReadOnlyCollection<ConfirmedCategoryAssignment> items,
        CancellationToken cancellationToken = default);
}

/// <summary>Подтверждённое пользователем присвоение категории товару (category может быть null = сброс).</summary>
public sealed record ConfirmedCategoryAssignment(Guid CommodityId, CommodityCategory? Category);