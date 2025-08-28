using System;
using System.Collections.Generic;
using System.Linq;
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
    Task<List<EndpointExternal>> GetBankEndpoints();
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

    public async Task<List<EndpointExternal>> GetBankEndpoints()
    {
        var (hasCachedValue, endpoints) = await memCache.TryGetEndpoints(EndpointsKey);

        if (!hasCachedValue)
        {
            endpoints = await ReadEndpointsAndCache();
        }

        return endpoints;
    }

    private async Task<List<EndpointExternal>> ReadEndpointsAndCache()
    {
        var file = await GetFileFromGithub();
        var engine = new DelimitedFileEngine<EndpointV2>(Encoding.UTF8);
        var endpoints = engine.ReadString(file).ToList();

        List<EndpointExternal> result = [];
        logger.LogInformation("Endpoints parsed from csv - {totalRecords} to be cached", engine.TotalRecords);

        if (engine.TotalRecords > 0 && endpoints.Count > 0)
        {
            result = await memCache.SetEndpointsCache(EndpointsKey, endpoints, TimeSpan.FromMinutes(300));
            logger.LogInformation("Cache refresh completed - total of {totalRecords} cached", engine.TotalRecords);
        }
        else
        {
            logger.LogCritical("Plugin func-esbits no endpoints found in csv");
        }

        return result;
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
