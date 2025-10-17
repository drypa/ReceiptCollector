using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

public sealed class MongoReceiptSourceOptions
{
    public const string SectionName = "Infrastructure:Receipts:Mongo";

    [Required]
    public string? ConnectionString { get; init; }

    [Required]
    public string? Database { get; init; }

    [Required]
    public string? Collection { get; init; }
}
