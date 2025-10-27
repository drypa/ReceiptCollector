namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public sealed class UserAuthLink
{
    public UserAuthLink(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, DateTimeOffset? usedAt = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Token hash must be provided.", nameof(tokenHash));
        }

        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= ExpiresAt;

    public bool IsUsed => UsedAt.HasValue;

    public void MarkAsUsed(DateTimeOffset usedAt)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Authentication link already used.");
        }

        UsedAt = usedAt;
    }
}
