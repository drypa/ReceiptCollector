using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

public sealed class UserAuthLinkOptions
{
    public const string SectionName = "Infrastructure:AuthLinks";

    [Required]
    [Url]
    public string? BaseUrl { get; init; }

    [Range(1, int.MaxValue)]
    public int LifetimeMinutes { get; init; } = 10;
}
