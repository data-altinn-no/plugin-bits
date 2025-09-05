using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dan.Common.Exceptions;
using Dan.Common.Models;
using Dan.Common.Util;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using Dan.Plugin.Bits.Services;
using FileHelpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Dan.Plugin.Bits;

public class Plugin

{ 
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private readonly Settings _settings;
    private readonly IMemoryCacheProvider _memCache;
    private readonly IControlInformationService _controlInformationService;
    private const string ENDPOINTS_KEY = "endpoints_key";   


    public Plugin(IOptions<Settings> settings, IControlInformationService controlInformationService, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IMemoryCacheProvider memCache)
    {
        _logger = loggerFactory.CreateLogger<Plugin>();
        _memCache = memCache;
        _client = httpClientFactory.CreateClient("SafeHttpClient");
        _settings = settings.Value;
        _controlInformationService = controlInformationService;
    }

    [Function(PluginConstants.Kontrollinformasjon)]
    public async Task<HttpResponseData> GetKontrollinformasjon(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext context)
    {
        return await EvidenceSourceResponse.CreateResponse(req, GetEvidenceValuesKontrollinformasjon);
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesKontrollinformasjon()
    {
        try
        {
            var ecb = new EvidenceBuilder(new Metadata(), PluginConstants.Kontrollinformasjon);
            var endpoints = await _controlInformationService.GetBankEndpoints();
            var json = JsonConvert.SerializeObject(endpoints);
            ecb.AddEvidenceValue(PluginConstants.DefaultValue, json, PluginConstants.SourceName, false);
            return ecb.GetEvidenceValues();
        }
        catch (JsonSerializationException e)
        {
            _logger.LogError(e, "Unable to parse bank endpoints response: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUnableToParseResponse, e.Message, e);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to fetch bank endpoints: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, e.Message, e);
        }
    }

    [Function(PluginConstants.KontrollinformasjonV2)]
    public async Task<HttpResponseData> GetKontrollinformasjonv2(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext context)
    {
        return await EvidenceSourceResponse.CreateResponse(req, GetEvidenceValuesKontrollinformasjonv2);
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesKontrollinformasjonv2()
    {
        try
        {
            var ecb = new EvidenceBuilder(new Metadata(), PluginConstants.Kontrollinformasjon);
            var endpoints = await _controlInformationService.GetBankEndpointsWithDates();
            var json = JsonConvert.SerializeObject(endpoints);
            ecb.AddEvidenceValue(PluginConstants.DefaultValue, json, PluginConstants.SourceName, false);
            return ecb.GetEvidenceValues();
        }
        catch (JsonSerializationException e)
        {
            _logger.LogError(e, "Unable to parse bank endpoints response: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUnableToParseResponse, e.Message, e);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to fetch bank endpoints: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, e.Message, e);
        }
    }

    [Function("OppdaterKontrollinformasjon")]
    public async Task<HttpResponseData> UpdateKontrollinformasjon(
           [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req,
           FunctionContext context)
    {
        var endpoints = await _controlInformationService.ReadEndpointsAndCache();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new EndpointsList() { Endpoints = endpoints, Total = endpoints.Count });
        return response;
    } 

    
}
