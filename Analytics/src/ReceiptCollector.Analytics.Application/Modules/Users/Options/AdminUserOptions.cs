using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Application.Modules.Users.Options;

public class AdminUserOptions
{
    public const string SectionName = "Infrastructure:AdminUsers";
    
    [Required]
    public List<int> TelegramIds { get; set; } = new();
}