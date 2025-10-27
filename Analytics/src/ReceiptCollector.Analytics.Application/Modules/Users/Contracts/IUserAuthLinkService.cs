namespace ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

public interface IUserAuthLinkService
{
    Task<UserAuthLinkResult> GenerateAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record UserAuthLinkResult(string Link, DateTimeOffset ExpiresAt);
