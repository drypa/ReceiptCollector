using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Integration tests for <see cref="UserAuthLinkRepository.TryMarkAsUsedAsync"/> against a real
/// relational database. The InMemory EF Core provider does not support
/// <c>ExecuteUpdateAsync</c>, so these tests verify the actual atomic
/// <c>UPDATE ... WHERE UsedAt IS NULL</c> semantics (including the race condition) on Postgres.
/// </summary>
public sealed class UserAuthLinkRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public UserAuthLinkRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task TryMarkAsUsedAsync_marks_link_as_used_and_returns_true()
    {
        await ClearDatabaseAsync();

        var link = CreateLink();
        await InsertAsync(link);

        var usedAt = DateTimeOffset.UtcNow;

        var success = await new UserAuthLinkRepository(CreateContext())
            .TryMarkAsUsedAsync(link.Id, usedAt, CancellationToken.None);

        Assert.True(success);

        var stored = await GetEntityByIdAsync(link.Id);
        Assert.NotNull(stored);
        Assert.NotNull(stored!.UsedAt);
        // Postgres timestamps have microsecond precision, so allow a small tolerance.
        Assert.Equal(usedAt, stored.UsedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TryMarkAsUsedAsync_returns_false_for_link_that_was_already_used()
    {
        await ClearDatabaseAsync();

        var link = CreateLink(usedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await InsertAsync(link);

        var success = await new UserAuthLinkRepository(CreateContext())
            .TryMarkAsUsedAsync(link.Id, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task TryMarkAsUsedAsync_returns_false_when_link_missing()
    {
        await ClearDatabaseAsync();

        var success = await new UserAuthLinkRepository(CreateContext())
            .TryMarkAsUsedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task TryMarkAsUsedAsync_concurrent_calls_allow_single_claim()
    {
        const int attempts = 8;

        await ClearDatabaseAsync();

        var link = CreateLink();
        await InsertAsync(link);

        // All attempts are released at the same moment; each one runs the UPDATE on its own
        // connection. The WHERE UsedAt IS NULL condition plus Postgres row locking guarantees
        // that exactly one attempt claims the link.
        using var barrier = new Barrier(attempts);

        var results = await Task.WhenAll(
            Enumerable.Range(0, attempts)
                .Select(_ => ClaimAsync(link.Id, barrier)));

        Assert.Equal(1, results.Count(success => success));
        Assert.Equal(attempts - 1, results.Count(success => !success));
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private async Task<bool> ClaimAsync(Guid linkId, Barrier barrier)
    {
        return await Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await using var context = CreateContext();
            var repository = new UserAuthLinkRepository(context);
            return await repository.TryMarkAsUsedAsync(linkId, DateTimeOffset.UtcNow, CancellationToken.None);
        });
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention();

        return new ReceiptDbContext(builder.Options);
    }

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.UserAuthLinks.ExecuteDeleteAsync();
    }

    private async Task InsertAsync(UserAuthLink link)
    {
        await using var context = CreateContext();
        await context.UserAuthLinks.AddAsync(UserAuthLinkEntity.FromDomain(link));
        await context.SaveChangesAsync();
    }

    private async Task<UserAuthLinkEntity?> GetEntityByIdAsync(Guid linkId)
    {
        await using var context = CreateContext();
        return await context.UserAuthLinks.AsNoTracking().FirstOrDefaultAsync(link => link.Id == linkId);
    }

    private static UserAuthLink CreateLink(DateTimeOffset? usedAt = null)
    {
        return new UserAuthLink(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "token-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(5),
            usedAt);
    }
}
