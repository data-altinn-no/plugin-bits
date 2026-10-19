using Microsoft.Extensions.Caching.Memory;

namespace Dan.Plugin.Bits.Services;

public interface IMemoryCacheProvider
{
    bool TryGet<T>(string key, out T value) where T : class;

    /// <summary>
    /// Stores a value with no expiry. Entries are replaced by the refresh flow (GitHub webhook / timer), not by time.
    /// </summary>
    T Set<T>(string key, T value) where T : class;
}

public class MemoryCacheProvider(IMemoryCache memoryCache) : IMemoryCacheProvider
{
    public bool TryGet<T>(string key, out T value) where T : class
    {
        return memoryCache.TryGetValue(key, out value);
    }

    public T Set<T>(string key, T value) where T : class
    {
        var options = new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove
        };

        return memoryCache.Set(key, value, options);
    }
}
