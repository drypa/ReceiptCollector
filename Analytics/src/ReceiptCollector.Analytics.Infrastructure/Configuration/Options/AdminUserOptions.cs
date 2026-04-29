using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

public class AdminUserOptions
{
    public const string SectionName = "Infrastructure:AdminUsers";
    
    [Required]
    public List<long> TelegramIds { get; set; } = new();
}