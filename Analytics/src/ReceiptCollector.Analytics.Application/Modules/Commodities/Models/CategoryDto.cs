namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Категория товара для UI-справочника.
/// </summary>
/// <param name="Id">Числовой идентификатор категории (значение enum CommodityCategory).</param>
/// <param name="Key">Имя enum CommodityCategory (например, "Food") — используется фронтом
/// как значение &lt;select&gt; и как строка категории в контракте PUT /api/receipts/{id}/categories.</param>
/// <param name="Name">Человекочитаемое отображаемое имя (display name).</param>
/// <param name="Group">Группа для &lt;optgroup&gt; ("" — без группы).</param>
public sealed record CategoryDto(int Id, string Key, string Name, string Group);