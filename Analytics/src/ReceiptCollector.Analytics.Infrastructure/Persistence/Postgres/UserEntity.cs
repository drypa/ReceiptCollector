using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public int TelegramId { get; set; }

    internal static UserEntity Create(User user)
    {
        return new UserEntity
        {
            Id = user.Id,
            Name = user.Name,
            ExternalId = user.ExternalId,
            TelegramId = user.TelegramId
        };
    }

    internal User MapToDomain()
    {
        return new User(Id, Name, ExternalId, TelegramId);
    }
}
