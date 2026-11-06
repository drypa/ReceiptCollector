namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

/// <summary>
/// Публичное представление магазина; переиспользуется в <c>ReceiptSummaryDto</c>/<c>ReceiptDetailsDto</c>
/// и в списке <c>GET /api/merchants</c>.
/// <para>
/// Аддитивные поля товарной категории по умолчанию (ADR-032, п. 2/6, FR-M1): хранятся отдельно
/// от <see cref="Category"/> магазина (разные справочники). Значения по умолчанию (<c>null</c>)
/// сохраняют совместимость всех существующих построений DTO; <c>null</c> = дефолт не задан.
/// </para>
/// </summary>
/// <param name="Id">Идентификатор магазина.</param>
/// <param name="Name">Название.</param>
/// <param name="Category">Категория самого магазина (<c>MerchantCategory</c>, не товарная).</param>
/// <param name="Address">Адрес.</param>
/// <param name="Inn">ИНН.</param>
/// <param name="DefaultCommodityCategoryId">
/// Код товарной категории по умолчанию (<c>CommodityCategory</c>) либо <c>null</c> — не задана
/// (включая невалидный код из БД, приведённый к <c>null</c> безопасным чтением, A1).
/// </param>
/// <param name="DefaultCommodityCategoryName">
/// Отображаемое имя категории по умолчанию (<c>CommodityCategoryHelper.GetDisplayName</c>)
/// либо <c>null</c> — не задана.
/// </param>
public sealed record MerchantDto(
    Guid Id,
    string Name,
    int Category,
    string? Address,
    string? Inn,
    int? DefaultCommodityCategoryId = null,
    string? DefaultCommodityCategoryName = null);
