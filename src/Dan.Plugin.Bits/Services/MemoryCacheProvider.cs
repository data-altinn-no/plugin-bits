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
    public Task<(bool success, List<EndpointExternal> result)> TryGetEndpoints(string key);

    public Task<List<EndpointExternal>> SetEndpointsCache(string key, List<EndpointV2> value, TimeSpan timeToLive);
}

public class MemoryCacheProvider(IMemoryCache memoryCache, IOptions<Settings> settings) : IMemoryCacheProvider
{
    private readonly Settings settings = settings.Value;

    public Task<(bool success, List<EndpointExternal> result)> TryGetEndpoints(string key)
    {
        var success = memoryCache.TryGetValue(key, out List<EndpointExternal> result);
        return Task.FromResult((success, result));
    }

    public async Task<List<EndpointExternal>> SetEndpointsCache(string key, List<EndpointV2> value, TimeSpan timeToLive)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
        {
            Priority = CacheItemPriority.High,
        };

        cacheEntryOptions.SetAbsoluteExpiration(timeToLive);
        var result = memoryCache.Set(key, MapToExternal(value), cacheEntryOptions);

        await Task.CompletedTask;

        return result;
    }

    private List<EndpointExternal> MapToExternal(List<EndpointV2> endpoints)
    {
        var query = from endpoint in endpoints
            select new EndpointExternal()
            {
                Env = settings.UseTestEndpoints ? "test" : "prod",
                Url = endpoint.Url,
                Name = endpoint.Navn,
                OrgNo = endpoint.OrgNummer,
                Version = endpoint.Version,
            };

        return query.ToList();
    }
}
