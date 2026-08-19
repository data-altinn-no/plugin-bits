using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Dan.Plugin.Bits.Services;

public interface IMemoryCacheProvider
{
    public Task<(bool success, IReadOnlyList<EndpointExternal> result)> TryGetEndpoints(string key);
    public Task<(bool success, IReadOnlyList<Limitation> result)> TryGetLimitations(string key);

    public List<EndpointExternal> SetEndpointsCache(string key, List<EndpointV2> value, TimeSpan timeToLive);
    public List<Limitation> SetLimitationsCache(string key, List<Limitation> value, TimeSpan timeToLive);
}

public class MemoryCacheProvider(IMemoryCache memoryCache, IOptions<Settings> settings) : IMemoryCacheProvider
{
    private readonly Settings settings = settings.Value;

    public async Task<(bool success, IReadOnlyList<EndpointExternal> result)> TryGetEndpoints(string key)
    {
        var success = memoryCache.TryGetValue(key, out IReadOnlyList<EndpointExternal> result);
        return (success, result);
    }

    public async Task<(bool success, IReadOnlyList<Limitation> result)> TryGetLimitations(string key)
    {
        var success = memoryCache.TryGetValue(key, out IReadOnlyList<Limitation> result);
        return (success, result);
    }

    public List<EndpointExternal> SetEndpointsCache(string key, List<EndpointV2> value, TimeSpan timeToLive)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
        {
            Priority = CacheItemPriority.High,
        };

        cacheEntryOptions.SetAbsoluteExpiration(timeToLive);
        var result = memoryCache.Set(key, MapToExternal(value), cacheEntryOptions);

        return result;
    }

    public List<Limitation> SetLimitationsCache(string key, List<Limitation> value, TimeSpan timeToLive)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
        {
            Priority = CacheItemPriority.High,
        };

        cacheEntryOptions.SetAbsoluteExpiration(timeToLive);
        var result = memoryCache.Set(key, value, cacheEntryOptions);

        return result;
    }

    private List<EndpointExternal> MapToExternal(List<EndpointV2> endpoints)
    {
        var env = settings.UseTestEndpoints ? "test" : "prod";

        var query = from endpoint in endpoints
            select new EndpointExternal()
            {
                Env = env,
                Url = endpoint.Url,
                Name = endpoint.Navn,
                OrgNo = endpoint.OrgNummer,
                Version = endpoint.Version,
                ToDate = endpoint.ToDate,
                FromDate = endpoint.FromDate
            };

        return query.ToList();
    }
}
