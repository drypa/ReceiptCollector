using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Users;

internal sealed class UserAuthLinkService : IUserAuthLinkService
{
    private readonly IUserAuthLinkRepository _authLinkRepository;
    private readonly IUserRepository _userRepository;
    private readonly UserAuthLinkOptions _options;

    public UserAuthLinkService(
        IUserAuthLinkRepository authLinkRepository,
        IUserRepository userRepository,
        IOptions<UserAuthLinkOptions> options)
    {
        _authLinkRepository = authLinkRepository ?? throw new ArgumentNullException(nameof(authLinkRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Auth link base url is not configured.");
        }
    }

    public async Task<UserAuthLinkResult> GenerateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id must be provided.", nameof(userId));
        }

        var userExists = await _userRepository.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false) is not null;
        if (!userExists)
        {
            throw new InvalidOperationException($"User '{userId}' does not exist.");
        }

        await _authLinkRepository.RemoveAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var utcNow = DateTimeOffset.UtcNow;
        var token = GenerateToken();
        var tokenHash = HashToken(token);
        var expiresAt = utcNow.AddMinutes(_options.LifetimeMinutes);

        var authLink = new UserAuthLink(Guid.NewGuid(), userId, tokenHash, utcNow, expiresAt);
        await _authLinkRepository.AddAsync(authLink, cancellationToken).ConfigureAwait(false);

        var link = BuildLink(_options.BaseUrl!, token);
        return new UserAuthLinkResult(link, expiresAt);
    }

    public async Task<UserAuthLinkResult> GenerateByTelegramIdAsync(int telegramId, CancellationToken cancellationToken)
    {
        if (telegramId <= 0)
        {
            throw new ArgumentException("Telegram id must be positive.", nameof(telegramId));
        }

        var user = await _userRepository.GetByTelegramIdAsync(telegramId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException($"User with telegram id '{telegramId}' does not exist.");
        }

        return await GenerateAsync(user.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAuthLinkValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return UserAuthLinkValidationResult.Failure("Token is required.");
        }

        var tokenHash = HashToken(token);
        var link = await _authLinkRepository.GetByTokenHashAsync(tokenHash, cancellationToken).ConfigureAwait(false);

        if (link is null)
        {
            return UserAuthLinkValidationResult.Failure("Token not found.");
        }

        var utcNow = DateTimeOffset.UtcNow;

        if (link.IsExpired(utcNow))
        {
            return UserAuthLinkValidationResult.Failure("Token expired.");
        }

        if (link.IsUsed)
        {
            return UserAuthLinkValidationResult.Failure("Token already used.");
        }

        await _authLinkRepository.MarkAsUsedAsync(link.Id, utcNow, cancellationToken).ConfigureAwait(false);

        return UserAuthLinkValidationResult.Success(link.UserId);
    }

    private static string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static string BuildLink(string baseUrl, string token)
    {
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{Endpoints.AuthGroup}{Endpoints.AuthByLinkPath}{separator}token={token}";
    }
}
