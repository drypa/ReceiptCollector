using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

public sealed class ReceiptSynchronizationOptions
{
    public const string SectionName = "Infrastructure:Receipts:Synchronization";

    [Range(1, int.MaxValue)]
    public int BatchSize { get; init; } = 100;

    [Required]
    public Guid? UserId { get; init; }

    /// <summary>
    /// Если <c>true</c>, синхронизация чеков при старте сервиса пропускается.
    /// По умолчанию <c>false</c> — синхронизация выполняется.
    /// </summary>
    public bool Skip { get; init; } = false;
}
