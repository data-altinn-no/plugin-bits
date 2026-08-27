using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Exceptions;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using FileHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits.Services;

public interface IControlInformationService
{
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpoints();
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpointsWithDates();

    Task<IReadOnlyList<EndpointExternal>> GetPantUtlegg();

    Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCachePantUtlegg();

    Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCache();
    Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCacheWithLimitations();
    Task<IReadOnlyList<Limitation>> GetBankLimitations(string orgNo);
}

public class ControlInformationService(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IOptions<Settings> settings,
    IMemoryCacheProvider memCache) : IControlInformationService
{
    private readonly ILogger logger = loggerFactory.CreateLogger<Plugin>();
    private readonly HttpClient client = httpClientFactory.CreateClient(Constants.SafeHttpClient);
    private readonly Settings settings = settings.Value;

    private const string EndpointsKey = "endpoints_key";
    private const string PantUtleggKey = "pantutlegg_key";
    private const string LimitationsKeyPrefix = "limitations_key";
    private const int ErrorKeyTemp = 1;

    public async Task<IReadOnlyList<EndpointExternal>> GetBankEndpoints()
    {
        var (hasCachedValue, endpoints) = await memCache.TryGetEndpoints(EndpointsKey);

        if (!hasCachedValue)
        {
            logger.LogInformation("No endpoints found in cache");
            endpoints = await ReadEndpointsAndCache();
        }

        logger.LogInformation("Returning total of {totalRecords} endpoints read from cache", endpoints.Count);

        //remove endpoints that are not currently active, they are returned only in KontrollinformasjonUtvidet
        //Limitations are only included in KontrollinformasjonUtvidet.
        return endpoints.Where(x =>
            (x.FromDate == null || x.FromDate <= DateTime.UtcNow) &&
            (x.ToDate == null || x.ToDate >= DateTime.UtcNow))
             .Select(x => new EndpointExternal
             {
                Env = x.Env,
                OrgNo = x.OrgNo,
                Url = x.Url,
                Version = x.Version,
                ToDate = null,
                FromDate = null,
                Name = x.Name,
                Limitations = null
             })
            .ToList();
    }

    public async Task<IReadOnlyList<EndpointExternal>> GetPantUtlegg()
    {
        var (hasCachedValue, endpoints) = await memCache.TryGetEndpoints(PantUtleggKey);

        if (!hasCachedValue)
        {
            logger.LogInformation("No PantUtlegg endpoints found in cache");
            endpoints = await ReadEndpointsAndCachePantUtlegg();
        }

        logger.LogInformation("Returning total of {totalRecords} PantUtlegg endpoints read from cache", endpoints.Count);

        return endpoints;
    }


    public async Task<IReadOnlyList<EndpointExternal>> GetBankEndpointsWithDates()
    {
        var (hasCachedValue, endpoints) = await memCache.TryGetEndpoints(EndpointsKey);

        if (!hasCachedValue)
        {
            logger.LogInformation("No endpoints found in cache");
            endpoints = await ReadEndpointsAndCache();
        }

        logger.LogInformation("Returning total of {totalRecords} endpoints read from cache", endpoints.Count);

        var activeEndpoints = endpoints.Where(x =>
            (x.ToDate == null || x.ToDate >= DateTime.UtcNow)).ToList();

        // KontrollinformasjonUtvidet is the only dataset that should carry limitations, so they're attached
        // here rather than in GetBankEndpoints. This mutates the EndpointExternal instances that live in the
        // shared endpoints cache, so once populated they stay attached to those objects until the cache entry
        // itself expires/refreshes - subsequent calls don't need to re-fetch limitations for the same endpoints.
        await AttachLimitations(activeEndpoints);

        return activeEndpoints;
    }


    public async Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCache()
    {
        return await ReadEndpointsFromGithubAndCache(settings.EndpointsResourceFile, EndpointsKey, "Bank endpoints");
    }

    public async Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCacheWithLimitations()
    {
        var endpoints = await ReadEndpointsAndCache();
        var activeEndpoints = endpoints.Where(x => x.ToDate == null || x.ToDate >= DateTime.UtcNow);

        await AttachLimitations(activeEndpoints);

        return endpoints;
    }

    private async Task AttachLimitations(IEnumerable<EndpointExternal> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Limitations = (await GetBankLimitations(endpoint.OrgNo)).ToList();
        }
    }

    public async Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCachePantUtlegg()
    {
        return await ReadEndpointsFromGithubAndCache(settings.PantUtleggResourceFile, PantUtleggKey, "PantUtlegg endpoints");
    }

    public async Task<IReadOnlyList<Limitation>> ReadLimitationsAndCache(string orgNo)
    {
        return await ReadLimitationsFromGithubAndCache(settings.LimitationsResourceFile ,orgNo, $"{LimitationsKeyPrefix}{orgNo}", "Limitations");
    }

    public async Task<IReadOnlyList<Limitation>> GetBankLimitations(string orgNo)
    {
        var cacheKey = $"{LimitationsKeyPrefix}{orgNo}";
        var (hasCachedValue, limitations) = await memCache.TryGetLimitations(cacheKey);

        if (!hasCachedValue)
        {
            logger.LogInformation("No limitations found in cache for {orgNo}", orgNo);
            limitations = await ReadLimitationsAndCache(orgNo);
        }

        logger.LogInformation("Returning total of {totalRecords} limitations for {orgNo} read from cache", limitations.Count, orgNo);
        return limitations;
    }

    private async Task<IReadOnlyList<EndpointExternal>> ReadEndpointsFromGithubAndCache(string resourceFilePath, string cacheKey, string resourceName)
    {
        try
        {
            var file = await GetFileFromGithub(resourceFilePath);
            var engine = new DelimitedFileEngine<EndpointV2>(Encoding.UTF8);
            var endpoints = engine.ReadString(file).ToList();

            List<EndpointExternal> result = [];
            logger.LogInformation("{resourceName} parsed from {resourceFile} csv - {totalRecords} to be cached with key {cacheKey}", 
                resourceName, resourceFilePath, engine.TotalRecords, cacheKey);

            if (engine.TotalRecords > 0 && endpoints.Count > 0)
            {
                result = memCache.SetEndpointsCache(cacheKey, endpoints, TimeSpan.FromMinutes(300));
                logger.LogInformation("Cache refresh completed for {resourceName} ({cacheKey}) - total of {totalRecords} cached", 
                    resourceName, cacheKey, engine.TotalRecords);
            }
            else
            {
                logger.LogCritical("Plugin func-esbits no {resourceName} found in csv for {resourceFile}", resourceName, resourceFilePath);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to fetch or parse {resourceName} from {resourceFile}: {message}", 
                resourceName, resourceFilePath, ex.Message);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, $"{resourceName} are currently unavailable", ex);
        }
    }

    private async Task<IReadOnlyList<Limitation>> ReadLimitationsFromGithubAndCache(string resourceFile, string orgNo, string cacheKey, string resourceName)
    {
        try
        {
            var file = await GetFileFromGithub(resourceFile + $"{orgNo}.json", allowNotFound: true);
            if (file == null)
            {
                logger.LogInformation("No {resourceName} file found on GitHub for {orgNo} at {resourceFile}", resourceName, orgNo, resourceFile);
                return memCache.SetLimitationsCache(cacheKey, [], TimeSpan.FromMinutes(300));
            }
            

            var limitations = JsonConvert.DeserializeObject<List<Limitation>>(file) ?? [];
            var result = memCache.SetLimitationsCache(cacheKey, limitations, TimeSpan.FromMinutes(300));
            logger.LogInformation("Cache refresh completed for {resourceName} ({cacheKey}) - total of {totalRecords} cached",
                resourceName, cacheKey, limitations.Count);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to fetch or parse {resourceName} for {orgNo} from {resourceFile}: {message}",
                resourceName, orgNo, resourceFile, ex.Message);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, $"{resourceName} are currently unavailable", ex);
        }
    }

    private async Task<string> GetFileFromGithub(string filePath, bool allowNotFound = false)
    {
        var url = $"https://api.github.com/repos/data-altinn-no/bits/contents/{filePath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GithubPat);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("plugin-bits");

        var response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        logger.LogCritical("Github retrieval failed for {filePath}, status code: {StatusCode}, reason: {ReasonPhrase}",
            filePath, response.StatusCode, response.ReasonPhrase);
        throw new EvidenceSourceTransientException(ErrorKeyTemp, "Banking endpoints are currently unavailable");
    }
}
