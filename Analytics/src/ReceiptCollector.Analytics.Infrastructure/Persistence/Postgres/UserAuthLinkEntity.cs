using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class UserAuthLinkEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    internal static UserAuthLinkEntity FromDomain(UserAuthLink link)
    {
        return new UserAuthLinkEntity
        {
            Id = link.Id,
            UserId = link.UserId,
            TokenHash = link.TokenHash,
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
            UsedAt = link.UsedAt
        };
    }

    internal UserAuthLink ToDomain()
    {
        return new UserAuthLink(Id, UserId, TokenHash, CreatedAt, ExpiresAt, UsedAt);
    }
}
