using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Дожим исторических позиций магазина категорией по умолчанию (ADR-032, п. 4, E1/FR-M3).
/// Сервис — тонкий оркестратор: проверка существования магазина и настроенного дефолта,
/// далее одна репозиторная операция <see cref="ICommodityRepository.ApplyMerchantDefaultForMerchantAsync"/>
/// (транзакция, предикат «без категории», <c>IgnoreQueryFilters()</c> с комментарием-маркером,
/// <c>TryAdd</c> в кэш после commit — всё внутри, T-04). Транзакцию на уровне сервиса дублировать
/// не нужно (ADR-032, п. 4); в <c>precategorized_commodities</c> сервис не пишет.
/// </summary>
internal sealed class MerchantDefaultCategoryService : IMerchantDefaultCategoryService
{
    private readonly IMerchantRepository _merchantRepository;
    private readonly ICommodityRepository _commodityRepository;
    private readonly ILogger<MerchantDefaultCategoryService> _logger;

    public MerchantDefaultCategoryService(
        IMerchantRepository merchantRepository,
        ICommodityRepository commodityRepository,
        ILogger<MerchantDefaultCategoryService> logger)
    {
        _merchantRepository = merchantRepository ?? throw new ArgumentNullException(nameof(merchantRepository));
        _commodityRepository = commodityRepository ?? throw new ArgumentNullException(nameof(commodityRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MerchantDefaultApplyResult> ApplyExistingAsync(
        Guid adminUserId,
        Guid merchantId,
        CancellationToken cancellationToken = default)
    {
        // Шаг 1 (ADR-032, п. 4): merchants не покрыты owner query-filter — GetByIdAsync без
        // IgnoreQueryFilters; отсутствующий магазин — рядовое состояние (кнопка по устаревшему списку).
        var merchant = await _merchantRepository
            .GetByIdAsync(merchantId, cancellationToken)
            .ConfigureAwait(false);

        if (merchant is null)
        {
            // Аудит неудачного нажатия на уровне сервиса — Debug (коды ответа даёт эндпоинт T-07).
            _logger.LogDebug(
                "Merchant default categorization skipped: merchant {MerchantId} not found (admin {AdminUserId}).",
                merchantId,
                adminUserId);
            return new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.NotFound, 0);
        }

        // Шаг 2: «дефолта нет» — это и явный NULL, и невалидный код справочника из БД,
        // приведённый к null безопасным чтением в MapToDomain (A1, ADR-032 п. 2).
        var defaultCategory = merchant.DefaultCommodityCategory;
        if (defaultCategory is null)
        {
            _logger.LogDebug(
                "Merchant default categorization skipped: default commodity category is not configured for merchant {MerchantId} (admin {AdminUserId}).",
                merchantId,
                adminUserId);
            return new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.DefaultNotConfigured, 0);
        }

        // Шаг 3: единственная точка записи 'merchant-default' для дожима — репозиторная операция
        // (T-04): идемпотентность, FR-M5 и пополнение кэша после commit гарантированы ею.
        // Ошибки инфраструктуры (сбой БД) намеренно не конвертируются в исход — пробрасываются
        // как есть (ADR-032, п. 4).
        var updatedCount = await _commodityRepository
            .ApplyMerchantDefaultForMerchantAsync(merchantId, defaultCategory.Value, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Applied merchant default commodity category {Category} to existing items of merchant {MerchantId} (admin {AdminUserId}): {UpdatedCount} items updated.",
            defaultCategory.Value,
            merchantId,
            adminUserId,
            updatedCount);

        return new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.Applied, updatedCount);
    }
}
