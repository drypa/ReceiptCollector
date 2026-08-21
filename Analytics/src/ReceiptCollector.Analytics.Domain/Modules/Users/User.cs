namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public class User
{
    public User(Guid id, string name, string externalId, int telegramId = 0, bool isAdmin = false)
    {
        Id = id;
        Name = name;
        ExternalId = externalId;
        TelegramId = telegramId;
        IsAdmin = isAdmin;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string ExternalId { get; }

    public int TelegramId { get; }

    public bool IsAdmin { get; }
}