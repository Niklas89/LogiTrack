using LogiTrack.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LogiTrack.Data;

// Shared by all requests in this process. Only one cache miss queries SQL at a time.
public sealed class InventoryCache(IMemoryCache cache) : IDisposable
{
    private readonly SemaphoreSlim fillGate = new(1, 1);
    private readonly object sync = new();
    private readonly object key = new();
    private long version;

    public async Task<List<InventoryItem>> GetAsync(LogiTrackContext context, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out List<InventoryItem>? items)) return items!;
        await fillGate.WaitAsync(cancellationToken);
        try
        {
            long readVersion;
            lock (sync)
            {
                if (cache.TryGetValue(key, out items)) return items!;
                readVersion = version;
            }
            items = await context.InventoryItems.AsNoTracking().OrderBy(item => item.ItemId)
                .ToListAsync(cancellationToken);
            lock (sync)
            {
                // A write during the query must not repopulate the cache with stale data.
                if (version == readVersion) cache.Set(key, items, TimeSpan.FromSeconds(30));
            }
            return items;
        }
        finally { fillGate.Release(); }
    }

    public void Invalidate()
    {
        lock (sync)
        {
            version++;
            cache.Remove(key);
        }
    }

    public void Dispose() => fillGate.Dispose();
}
