using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;

internal sealed class ReceiptReadService : IReceiptReadService
{
    private readonly ReceiptDbContext _dbContext;

    public ReceiptReadService(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        // MerchantDto.name категории дефолта формируется на C# (CommodityCategoryHelper),
        // поэтому SQL-проекция отдаёт сырые колонки receipt + merchant, а DTO собираются в памяти
        // (ADR-032, п. 2/T-10; JOIN merchants уже был нужен для имени/категории магазина).
        var rows = await _dbContext.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId)
            .OrderByDescending(receipt => receipt.PurchasedAt)
            .Skip(offset)
            .Take(limit)
            .Select(receipt => new ReceiptSummaryRow(
                receipt.Id,
                receipt.TotalAmount,
                receipt.PurchasedAt,
                receipt.Merchant.Id,
                receipt.Merchant.Name,
                (int)receipt.Merchant.Category,
                receipt.Merchant.Address,
                receipt.Merchant.Inn,
                receipt.Merchant.DefaultCommodityCategoryId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToSummaryDto).ToList();
    }

    public async Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReceiptDetailsDto?> GetByIdAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Receipts
            .AsNoTracking()
            .Include(receipt => receipt.Merchant)
            .Include(receipt => receipt.Items)
            .FirstOrDefaultAsync(receipt => receipt.Id == receiptId && receipt.UserId ==userId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return ToReceiptDto(entity);
    }

    private static ReceiptDetailsDto ToReceiptDto(ReceiptEntity entity)
    {
        var items = entity.Items
            .OrderBy(item => item.Name)
            .Select(item => new ReceiptItemDto(
                item.Id,
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice,
                item.CategoryId))
            .ToList();

        return new ReceiptDetailsDto(
            entity.Id,
            ToMerchantDto(
                entity.Merchant.Id,
                entity.Merchant.Name,
                (int)entity.Merchant.Category,
                entity.Merchant.Address,
                entity.Merchant.Inn,
                entity.Merchant.DefaultCommodityCategoryId),
            entity.TotalAmount,
            entity.PurchasedAt,
            items);
    }

    public async Task<IReadOnlyCollection<ReceiptSummaryDto>> GetByMerchantIdAsync(Guid userId, Guid merchantId, int limit = 10, int offset = 0, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        // См. GetRecentAsync: сырые колонки в SQL, MerchantDto (с именем дефолта из C#-словаря) — в памяти.
        var rows = await _dbContext.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId && receipt.MerchantId == merchantId)
            .OrderByDescending(receipt => receipt.PurchasedAt)  // Consistent ordering for pagination
            .Skip(offset)
            .Take(limit)
            .Select(receipt => new ReceiptSummaryRow(
                receipt.Id,
                receipt.TotalAmount,
                receipt.PurchasedAt,
                receipt.Merchant.Id,
                receipt.Merchant.Name,
                (int)receipt.Merchant.Category,
                receipt.Merchant.Address,
                receipt.Merchant.Inn,
                receipt.Merchant.DefaultCommodityCategoryId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToSummaryDto).ToList();
    }

    public async Task<int> GetTotalCountByMerchantIdAsync(Guid userId, Guid merchantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId && receipt.MerchantId == merchantId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Промежуточная SQL-проекция строки сводки: поля чека + колонки магазина, включая
    /// сырой код дефолтной товарной категории (имя формируется в <see cref="ToMerchantDto"/>,
    /// ADR-032, п. 2/T-10).
    /// </summary>
    private sealed record ReceiptSummaryRow(
        Guid ReceiptId,
        decimal TotalAmount,
        DateTime PurchasedAt,
        Guid MerchantId,
        string MerchantName,
        int MerchantCategory,
        string? MerchantAddress,
        string? MerchantInn,
        int? MerchantDefaultCommodityCategoryId);

    /// <summary>
    /// <see cref="MerchantDto"/> из колонок merchants. Сырой код дефолтной категории проходит
    /// безопасное чтение (ADR-032, A1 — тот же фильтр, что <c>MerchantEntity.MapToDomain</c>:
    /// NULL / 0 / код вне справочника → «не задана»); отображаемое имя формируется на C#
    /// (<c>CommodityCategoryHelper.GetDisplayName</c>, прецедент <c>MerchantEndpoints.ToDto</c>) —
    /// в SQL-проекцию оно не кладётся.
    /// </summary>
    private static MerchantDto ToMerchantDto(
        Guid id,
        string name,
        int category,
        string? address,
        string? inn,
        int? defaultCommodityCategoryId)
    {
        CommodityCategory? defaultCategory =
            defaultCommodityCategoryId is int code
            && code != 0
            && Enum.IsDefined(typeof(CommodityCategory), code)
                ? (CommodityCategory)code
                : null;

        return new MerchantDto(
            id,
            name,
            category,
            address,
            inn,
            defaultCategory is null ? null : (int)defaultCategory,
            defaultCategory is null ? null : CommodityCategoryHelper.GetDisplayName(defaultCategory.Value));
    }

    private static ReceiptSummaryDto ToSummaryDto(ReceiptSummaryRow row) => new(
        row.ReceiptId,
        ToMerchantDto(
            row.MerchantId,
            row.MerchantName,
            row.MerchantCategory,
            row.MerchantAddress,
            row.MerchantInn,
            row.MerchantDefaultCommodityCategoryId),
        row.TotalAmount,
        row.PurchasedAt);
}
