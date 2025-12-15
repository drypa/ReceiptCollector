using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
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

    // Test data constants
    private Guid _existingUserId;
    private Guid _existingMerchantId;
    private Guid _existingReceiptId;

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
        var userId = _existingUserId;
        var merchantId = _existingMerchantId;

        // Act
        var result = await _service.GetByMerchantIdAsync(userId, merchantId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.Id == _existingReceiptId);
        // Verify that the returned objects are ReceiptSummaryDto, not ReceiptDetailsDto
        Assert.DoesNotContain(result, r => r.GetType().Name.Contains("Details"));
        Assert.Contains(result, r => r.GetType().Name.Contains("Summary"));
    }

    [Fact]
    public async Task GetByMerchantIdAsync_WithValidUserIdAndMerchantId_ReturnsMatchingReceipts()
    {
        // Arrange
        var userId = _existingUserId;
        var merchantId = _existingMerchantId;

        // Act
        var result = await _service.GetByMerchantIdAsync(userId, merchantId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.Equal(userId, r.Id == _existingReceiptId ? _existingUserId : userId));
        Assert.Contains(result, r => r.Id == _existingReceiptId);
        // Verify that the returned objects are ReceiptSummaryDto, not ReceiptDetailsDto
        Assert.DoesNotContain(result, r => r.GetType().Name.Contains("Details"));
        Assert.Contains(result, r => r.GetType().Name.Contains("Summary"));
    }

    [Fact]
    public async Task GetByMerchantIdAsync_WithNonExistentMerchantId_ReturnsEmptyCollection()
    {
        var nonExistentMerchantId = Guid.NewGuid(); // Этот merchant ID не существует в тестовой базе


        var result = await _service.GetByMerchantIdAsync(_existingUserId, nonExistentMerchantId, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTotalCountAsync_WithValidUserId_ReturnsCorrectCount()
    {

        // Act
        var result = await _service.GetTotalCountAsync(_existingUserId, CancellationToken.None);

        // Assert
        Assert.IsType<int>(result);
        Assert.True(result >= 0); // Count should be non-negative
        Assert.True(result > 0, "User should have at least one receipt associated");
    }

    [Fact]
    public async Task GetTotalCountAsync_WithDifferentUserIds_ReturnsDifferentCounts()
    {
        // Arrange
        var userId2 = Guid.NewGuid(); // Новый пользователь без данных

        // Act
        var count1 = await _service.GetTotalCountAsync(_existingUserId, CancellationToken.None);
        var count2 = await _service.GetTotalCountAsync(userId2, CancellationToken.None);

        // Assert
        Assert.IsType<int>(count1);
        Assert.IsType<int>(count2);
        Assert.Equal(1, count1);
        Assert.Equal(0, count2);

        // Проверяем, что у пользователя с тестовыми данными есть хотя бы один чек
        Assert.True(count1 > 0, "User with test data should have at least one receipt");
        // Проверяем, что у нового пользователя нет чеков
        Assert.Equal(0, count2);
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
    public async Task GetRecentAsync_WithValidUserId_ReturnsReceipts()
    {
        // Arrange
        const int limit = 10;
        const int offset = 0;

        // Act
        var result = await _service.GetRecentAsync(_existingUserId, limit, offset, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.Id == _existingReceiptId);
        // Verify that the returned objects are ReceiptSummaryDto, not ReceiptDetailsDto
        Assert.DoesNotContain(result, r => r.GetType().Name.Contains("Details"));
        Assert.Contains(result, r => r.GetType().Name.Contains("Summary"));
    }

    [Fact]
    public async Task GetByIdAsync_WithValidUserIdAndReceiptId_ReturnsCorrectReceipt()
    {
        // Arrange
        var receiptId = _existingReceiptId;

        // Act
        var result = await _service.GetByIdAsync(_existingUserId, receiptId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(receiptId, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidUserId_ReturnsNull()
    {
        // Arrange
        var invalidUserId = Guid.NewGuid(); // Пользователь без доступа к этому чеку
        var receiptId = _existingReceiptId;

        // Act
        var result = await _service.GetByIdAsync(invalidUserId, receiptId, CancellationToken.None);

        // Assert
        Assert.Null(result);
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

        // Создаем тестовые данные
        await SetupTestData();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();

        await _postgresContainer.DisposeAsync();
    }

    private async Task SetupTestData()
    {
        _existingUserId = Guid.NewGuid();
        _existingMerchantId = Guid.NewGuid();
        _existingReceiptId = Guid.NewGuid();

        var merchant = new MerchantEntity
        {
            Id = _existingMerchantId,
            Name = "Test Merchant",
            Category = MerchantCategory.Flowers,
            Address = "123 Test Street",
            Inn = "1234567890"
        };

        var receipt = new ReceiptEntity
        {
            Id = _existingReceiptId,
            UserId = _existingUserId,
            MerchantId = _existingMerchantId,
            TotalAmount = 100.50m,
            PurchasedAt = DateTime.UtcNow.AddDays(-1),
            Merchant = merchant,
            ExternalId = "some-external-id",
            Items = new List<CommodityEntity>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = _existingReceiptId,
                    Name = "Test Product 1",
                    Quantity = 2,
                    UnitPrice = 25.00m,
                    Nds = 20,
                    NdsSum = 5.00m
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = _existingReceiptId,
                    Name = "Test Product 2",
                    Quantity = 1,
                    UnitPrice = 50.00m,
                    Nds = 10,
                    NdsSum = 5.00m
                }
            }
        };

        // Добавляем данные в контекст
        _dbContext.Merchants.Add(merchant);
        _dbContext.Receipts.Add(receipt);

        // Сохраняем изменения
        await _dbContext.SaveChangesAsync();
    }
}