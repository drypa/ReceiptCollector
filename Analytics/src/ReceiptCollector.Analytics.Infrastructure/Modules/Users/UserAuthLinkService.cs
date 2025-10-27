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
        return $"{baseUrl}{separator}token={token}";
    }
}
