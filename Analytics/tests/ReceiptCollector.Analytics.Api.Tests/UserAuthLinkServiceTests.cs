using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests;

public class UserAuthLinkServiceTests
{
    [Fact]
    public async Task GenerateAsync_creates_link_and_persists_entity()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);

        var userId = Guid.NewGuid();

        await context.Users.AddAsync(new UserEntity
        {
            Id = userId,
            Name = "Test User",
            ExternalId = "external",
            TelegramId = 123
        });

        await context.SaveChangesAsync();

        var userRepository = new UserRepository(context);
        var authLinkRepository = new UserAuthLinkRepository(context);
        var service = new UserAuthLinkService(
            authLinkRepository,
            userRepository,
            Options.Create(new UserAuthLinkOptions
            {
                BaseUrl = "https://app.example.com/login",
                LifetimeMinutes = 5
            }));

        var result = await service.GenerateAsync(userId, CancellationToken.None);

        Assert.StartsWith("https://app.example.com/login", result.Link, StringComparison.Ordinal);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);

        var persisted = await authLinkRepository.GetActiveByUserIdAsync(userId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(userId, persisted!.UserId);
    }

    [Fact]
    public async Task GenerateAsync_replaces_existing_link()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);

        var userId = Guid.NewGuid();

        await context.Users.AddAsync(new UserEntity
        {
            Id = userId,
            Name = "Test User",
            ExternalId = "external",
            TelegramId = 123
        });

        await context.SaveChangesAsync();

        var userRepository = new UserRepository(context);
        var authLinkRepository = new UserAuthLinkRepository(context);
        var service = new UserAuthLinkService(
            authLinkRepository,
            userRepository,
            Options.Create(new UserAuthLinkOptions
            {
                BaseUrl = "https://app.example.com/login",
                LifetimeMinutes = 5
            }));

        var first = await service.GenerateAsync(userId, CancellationToken.None);
        var second = await service.GenerateAsync(userId, CancellationToken.None);

        Assert.NotEqual(first.Link, second.Link);

        var links = await context.UserAuthLinks.AsNoTracking().Where(link => link.UserId == userId).ToListAsync();
        Assert.Single(links);
    }

    [Fact]
    public async Task GenerateAsync_throws_when_user_missing()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);

        var userRepository = new UserRepository(context);
        var authLinkRepository = new UserAuthLinkRepository(context);
        var service = new UserAuthLinkService(
            authLinkRepository,
            userRepository,
            Options.Create(new UserAuthLinkOptions
            {
                BaseUrl = "https://app.example.com/login",
                LifetimeMinutes = 5
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_returns_success_for_valid_token()
    {
        var userId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var link = CreateLink(linkId, userId, used: false);

        var repository = Substitute.For<IUserAuthLinkRepository>();
        repository.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(link);

        // Models the atomic "UPDATE ... WHERE UsedAt IS NULL" semantics: only the first
        // call wins, subsequent calls observe the link as already used.
        var claimCount = 0;
        repository.TryMarkAsUsedAsync(linkId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Interlocked.Exchange(ref claimCount, 1) == 0));

        var service = CreateService(repository);

        var validation = await service.ValidateAsync("token", CancellationToken.None);

        Assert.True(validation.IsValid);
        Assert.Equal(userId, validation.UserId);

        var secondAttempt = await service.ValidateAsync("token", CancellationToken.None);

        Assert.False(secondAttempt.IsValid);
        Assert.Equal("Token already used.", secondAttempt.Error);
    }

    [Fact]
    public async Task ValidateAsync_parallel_calls_with_same_token_allow_only_single_authentication()
    {
        const int attempts = 16;

        var userId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var link = CreateLink(linkId, userId, used: false);

        var repository = Substitute.For<IUserAuthLinkRepository>();
        repository.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(link);

        // Interlocked.Exchange makes the claim counter atomic, so exactly one of the
        // concurrent calls observes the initial state and wins - mirroring the DB-level
        // atomicity of "UPDATE ... WHERE UsedAt IS NULL".
        var claimCount = 0;
        repository.TryMarkAsUsedAsync(linkId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Interlocked.Exchange(ref claimCount, 1) == 0));

        var service = CreateService(repository);

        var results = await Task.WhenAll(
            Enumerable.Range(0, attempts)
                .Select(_ => service.ValidateAsync("token", CancellationToken.None)));

        Assert.Single(results.Where(result => result.IsValid));
        Assert.Equal(attempts - 1, results.Count(result => !result.IsValid && result.Error == "Token already used."));
    }

    [Fact]
    public async Task ValidateAsync_fails_when_token_expired()
    {
        var link = new UserAuthLink(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "token-hash",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1));

        var repository = Substitute.For<IUserAuthLinkRepository>();
        repository.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(link);

        var service = CreateService(repository);

        var validation = await service.ValidateAsync("token", CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal("Token expired.", validation.Error);
    }

    [Fact]
    public async Task ValidateAsync_returns_failure_when_token_missing()
    {
        var repository = Substitute.For<IUserAuthLinkRepository>();
        var service = CreateService(repository);

        var validation = await service.ValidateAsync(string.Empty, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal("Token is required.", validation.Error);
        await repository.DidNotReceive().GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static UserAuthLinkService CreateService(IUserAuthLinkRepository repository)
    {
        return new UserAuthLinkService(
            repository,
            Substitute.For<IUserRepository>(),
            Options.Create(new UserAuthLinkOptions
            {
                BaseUrl = "https://app.example.com/login",
                LifetimeMinutes = 5
            }));
    }

    private static UserAuthLink CreateLink(Guid id, Guid userId, bool used)
    {
        return new UserAuthLink(
            id,
            userId,
            "token-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(5),
            used ? DateTimeOffset.UtcNow : null);
    }
}
