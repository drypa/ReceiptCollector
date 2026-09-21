namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Запись кэша ранее присвоенных категорий (таблица commodity_category_assignments).
/// Кэш — сквозной для всех пользователей: поле UserId отсутствует (решение заказчика, ADR 009, C1/C4).
/// </summary>
public sealed class CommodityCategoryAssignment
{
    public Guid Id { get; }

    public string NormalizedName { get; }

    public string Name { get; }

    public int CategoryId { get; }

    public string CategoryName { get; }

    public DateTime UpdatedAt { get; }

    public CommodityCategoryAssignment(
        Guid id,
        string normalizedName,
        string name,
        int categoryId,
        string categoryName,
        DateTime updatedAt)
    {
        Id = id;
        NormalizedName = normalizedName;
        Name = name;
        CategoryId = categoryId;
        CategoryName = categoryName;
        UpdatedAt = updatedAt;
    }
}