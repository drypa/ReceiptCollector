using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
}
