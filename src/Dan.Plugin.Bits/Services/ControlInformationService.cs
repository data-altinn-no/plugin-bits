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

namespace Dan.Plugin.Bits.Services;

public interface IControlInformationService
{
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpoints();
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpointsWithDates();

    Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCache();
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
                Name = x.Name
             })
            .ToList();
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
        return endpoints.Where(x =>
            (x.ToDate == null || x.ToDate >= DateTime.UtcNow)).ToList();
    }


    public async Task<IReadOnlyList<EndpointExternal>> ReadEndpointsAndCache()
    {
        try { 
            var file = await GetFileFromGithub();
            var engine = new DelimitedFileEngine<EndpointV2>(Encoding.UTF8);
            var endpoints = engine.ReadString(file).ToList();

            List<EndpointExternal> result = [];
            logger.LogInformation("Endpoints parsed from csv - {totalRecords} to be cached", engine.TotalRecords);

            if (engine.TotalRecords > 0 && endpoints.Count > 0)
            {
                result = memCache.SetEndpointsCache(EndpointsKey, endpoints, TimeSpan.FromMinutes(300));
                logger.LogInformation("Cache refresh completed - total of {totalRecords} cached", engine.TotalRecords);
            }
            else
            {
                logger.LogCritical("Plugin func-esbits no endpoints found in csv");
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to fetch or parse bank endpoints: {message}", ex.Message);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, "Banking endpoints are currently unavailable", ex);
        }
    }

    private async Task<string> GetFileFromGithub()
    {
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.GithubPat);
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("plugin-bits"); // GitHub requires a User-Agent header

        var url = $"https://api.github.com/repos/data-altinn-no/bits/contents/{settings.EndpointsResourceFile}";
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }

        logger.LogCritical("Github retrieval failed for banking endpoints, status code: {StatusCode}, reason: {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
        throw new EvidenceSourceTransientException(ErrorKeyTemp, "Banking endpoints are currently unavailable");
    }
}
