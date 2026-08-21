namespace ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

public interface IUserAuthLinkService
{
    Task<UserAuthLinkResult> GenerateAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAuthLinkResult> GenerateByTelegramIdAsync(int telegramId, CancellationToken cancellationToken);

    Task<UserAuthLinkValidationResult> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed record UserAuthLinkResult(string Link, DateTimeOffset ExpiresAt);

public sealed record UserAuthLinkValidationResult(bool IsValid, Guid? UserId = null, string? Error = null, string? redirectUrl=null)
{
    public static UserAuthLinkValidationResult Success(Guid userId, string redirectUrl) => new(true, userId, null, redirectUrl);

    public static UserAuthLinkValidationResult Failure(string? error = null) => new(false, null, error);
}
