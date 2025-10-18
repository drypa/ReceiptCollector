namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public class User
{
    public User(Guid id, string name, string externalId)
    {
        Id = id;
        Name = name;
        ExternalId = externalId;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string ExternalId { get; }
}