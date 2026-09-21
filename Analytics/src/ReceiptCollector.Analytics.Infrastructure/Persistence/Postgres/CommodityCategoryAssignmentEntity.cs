using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class CommodityCategoryAssignmentEntity
{
    public Guid Id { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    internal static CommodityCategoryAssignmentEntity Create(
        string name,
        string normalizedName,
        CommodityCategory category,
        DateTime timestamp)
    {
        return new CommodityCategoryAssignmentEntity
        {
            Id = Guid.NewGuid(),
            NormalizedName = normalizedName,
            Name = name,
            CategoryId = (int)category,
            CategoryName = CommodityCategoryHelper.GetDisplayName(category),
            UpdatedAt = NormalizeUtc(timestamp)
        };
    }

    internal CommodityCategoryAssignment MapToDomain()
    {
        return new CommodityCategoryAssignment(
            Id,
            NormalizedName,
            Name,
            CategoryId,
            CategoryName,
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc));
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
    }
}