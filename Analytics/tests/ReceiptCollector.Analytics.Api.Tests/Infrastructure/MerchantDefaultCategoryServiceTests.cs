using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты сервиса дожима исторических позиций магазина (T-06, ADR-032 п. 4, E1/FR-M3):
/// исход-маппинг NotFound / DefaultNotConfigured / Applied (в т.ч. обновлено 0 — успех,
/// идемпотентность), вызов репозиторной операции с верной категорией, проброс ошибок
/// инфраструктуры (не конвертируются в исход). Репозитории замоканы (NSubstitute);
/// транзакция/кэш/предикат — ответственность репозитория (проверено в T-04).
/// </summary>
public sealed class MerchantDefaultCategoryServiceTests
{
    private readonly IMerchantRepository _merchantRepository = Substitute.For<IMerchantRepository>();
    private readonly ICommodityRepository _commodityRepository = Substitute.For<ICommodityRepository>();
    private readonly MerchantDefaultCategoryService _service;

    public MerchantDefaultCategoryServiceTests()
    {
        _service = new MerchantDefaultCategoryService(
            _merchantRepository,
            _commodityRepository,
            NullLogger<MerchantDefaultCategoryService>.Instance);
    }

    [Fact]
    public async Task ApplyExistingAsync_merchant_not_found_returns_not_found_and_does_not_apply()
    {
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns((Merchant?)null);

        var result = await _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None);

        Assert.Equal(MerchantDefaultApplyOutcome.NotFound, result.Outcome);
        Assert.Equal(0, result.UpdatedCount);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .ApplyMerchantDefaultForMerchantAsync(default, default, default);
    }

    [Fact]
    public async Task ApplyExistingAsync_default_not_configured_returns_default_not_configured()
    {
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(CreateMerchant(merchantId, defaultCommodityCategory: null));

        var result = await _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None);

        Assert.Equal(MerchantDefaultApplyOutcome.DefaultNotConfigured, result.Outcome);
        Assert.Equal(0, result.UpdatedCount);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .ApplyMerchantDefaultForMerchantAsync(default, default, default);
    }

    [Fact]
    public async Task ApplyExistingAsync_invalid_registry_code_read_as_null_returns_default_not_configured()
    {
        // Невалидный код справочника из БД (например, категория удалена между релизами)
        // безопасно читается как null в MapToDomain (ADR-032, A1) — на этом уровне домен
        // неотличим от «дефолт не задан»: тот же исход, применения нет. Фактическая проверка
        // safe-read (валидный/невалидный код/0/NULL → домен) — в MapToDomain-тестах T-02
        // (MerchantEntityMappingTests); этот тест фиксирует поведение границы сервиса.
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(CreateMerchant(merchantId, defaultCommodityCategory: null));

        var result = await _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None);

        Assert.Equal(MerchantDefaultApplyOutcome.DefaultNotConfigured, result.Outcome);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .ApplyMerchantDefaultForMerchantAsync(default, default, default);
    }

    [Fact]
    public async Task ApplyExistingAsync_configured_default_calls_repository_and_propagates_counter()
    {
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(CreateMerchant(merchantId, CommodityCategory.Dairy));
        _commodityRepository.ApplyMerchantDefaultForMerchantAsync(merchantId, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None);

        Assert.Equal(MerchantDefaultApplyOutcome.Applied, result.Outcome);
        Assert.Equal(7, result.UpdatedCount);
        await _commodityRepository.Received(1)
            .ApplyMerchantDefaultForMerchantAsync(merchantId, CommodityCategory.Dairy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyExistingAsync_zero_updated_is_successful_applied()
    {
        // Повторное нажатие кнопки: все позиции уже категоризированы — 0 строк, это успех
        // (идемпотентность, edge case задачи).
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(CreateMerchant(merchantId, CommodityCategory.Bakery));
        _commodityRepository.ApplyMerchantDefaultForMerchantAsync(merchantId, CommodityCategory.Bakery, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None);

        Assert.Equal(MerchantDefaultApplyOutcome.Applied, result.Outcome);
        Assert.Equal(0, result.UpdatedCount);
    }

    [Fact]
    public async Task ApplyExistingAsync_repository_failure_propagates_and_is_not_converted_to_outcome()
    {
        // Сбой инфраструктуры (БД) — не рядовое бизнес-состояние: исключение пробрасывается
        // как есть, его перехватывает глобальное правило запроса (500), а не сервис (ADR-032, п. 4).
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(CreateMerchant(merchantId, CommodityCategory.Dairy));
        _commodityRepository.ApplyMerchantDefaultForMerchantAsync(merchantId, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyExistingAsync(Guid.NewGuid(), merchantId, CancellationToken.None));
    }

    private static Merchant CreateMerchant(Guid id, CommodityCategory? defaultCommodityCategory)
        => new(id, "Магазин у дома", defaultCommodityCategory: defaultCommodityCategory);
}
