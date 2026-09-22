using System.Collections.Concurrent;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

internal sealed class InMemoryCommodityCategoryCache : ICommodityCategoryCache
{
    private readonly ConcurrentDictionary<string, CommodityCategory> _items = new();
    private readonly int _maxSize;
    private readonly object _gate = new();

    public InMemoryCommodityCategoryCache(int maxSize)
    {
        _maxSize = maxSize;
    }

    public int Count => _items.Count;

    public bool TryGet(string normalizedName, out CommodityCategory category)
        => _items.TryGetValue(normalizedName, out category);

    public bool TryAdd(string normalizedName, CommodityCategory category)
    {
        // MaxSize < 1 — кэш отключён; Undefined не хранится (FR-1.2).
        if (_maxSize < 1 || category == CommodityCategory.Undefined)
        {
            return false;
        }

        // LOCK: атомарность нужна только для проверки лимита и гонки «два одинаковых ключа».
        // Допущение: при гонке возможна незначительная переоценка лимита — без вытеснения (ADR 019, «Риски»).
        lock (_gate)
        {
            if (_items.ContainsKey(normalizedName) || _items.Count >= _maxSize)
            {
                return false; // first-wins (FR-1.4) / «заморозка» (FR-1.5)
            }

            return _items.TryAdd(normalizedName, category);
        }
    }
}