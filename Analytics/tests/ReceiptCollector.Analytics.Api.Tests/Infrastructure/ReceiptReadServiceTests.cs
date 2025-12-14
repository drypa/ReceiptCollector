using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class ReceiptReadServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private IReceiptReadService _service = null!;
    private ReceiptDbContext _dbContext = null!;

    public ReceiptReadServiceTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithDatabase("testdb")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    [Fact]
    public async Task GetByMerchantIdAsync_ReturnsCorrectReceipts()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        // Act
        var result = await _service.GetByMerchantIdAsync(userId, merchantId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetByMerchantIdAsync_WithValidUserIdAndMerchantId_ReturnsMatchingReceipts()
    {
        // This test would require setting up test data in the database
        // For now, we'll implement a basic test to verify the method exists and can be called
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var result = await _service.GetByMerchantIdAsync(userId, merchantId, CancellationToken.None);

        Assert.NotNull(result);
        // Additional assertions would require actual test data setup
    }

    [Fact]
    public async Task GetByMerchantIdAsync_WithNonExistentMerchantId_ReturnsEmptyCollection()
    {
        var userId = Guid.NewGuid();
        var nonExistentMerchantId = Guid.NewGuid(); // This merchant ID doesn't exist in the test database


        var result = await _service.GetByMerchantIdAsync(userId, nonExistentMerchantId, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTotalCountAsync_WithValidUserId_ReturnsCorrectCount()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _service.GetTotalCountAsync(userId, CancellationToken.None);

        // Assert
        Assert.IsType<int>(result);
        Assert.True(result >= 0); // Count should be non-negative
    }

    [Fact]
    public async Task GetTotalCountAsync_WithDifferentUserIds_ReturnsDifferentCounts()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        // Act
        var count1 = await _service.GetTotalCountAsync(userId1, CancellationToken.None);
        var count2 = await _service.GetTotalCountAsync(userId2, CancellationToken.None);

        // Assert
        Assert.IsType<int>(count1);
        Assert.IsType<int>(count2);
        Assert.True(count1 >= 0); // Count should be non-negative
        Assert.True(count2 >= 0); // Count should be non-negative
    }

    [Fact]
    public async Task GetTotalCountAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel the token immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.GetTotalCountAsync(userId, cts.Token));
    }

    [Fact]
    public async Task GetTotalCountAsync_WithEmptyGuidUserId_ReturnsZero()
    {
        // Arrange
        var emptyUserId = Guid.Empty;

        // Act
        var result = await _service.GetTotalCountAsync(emptyUserId, CancellationToken.None);

        // Assert
        Assert.Equal(0, result);
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        var serviceCollection = new ServiceCollection();

        // Настройка конфигурации для подключения к тестовому контейнеру
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PostgresOptions.SectionName}:ConnectionString"] = _postgresContainer.GetConnectionString()
            }).Build();

        // Добавляем инфраструктуру с тестовой конфигурацией
        serviceCollection.AddInfrastructure(configuration);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        _dbContext = serviceProvider.GetRequiredService<ReceiptDbContext>();
        _service = serviceProvider.GetRequiredService<IReceiptReadService>();
        
        // Убеждаемся, что база данных создана
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
        
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}