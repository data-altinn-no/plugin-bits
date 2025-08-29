using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dan.Common.Exceptions;
using Dan.Common.Models;
using Dan.Common.Util;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits;

public class Plugin(
    IControlInformationService controlInformationService,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<Plugin>();

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
            var endpoints = await controlInformationService.GetBankEndpoints();
            var json = JsonConvert.SerializeObject(endpoints);
            ecb.AddEvidenceValue(PluginConstants.DefaultValue, json, PluginConstants.SourceName, false);
            return ecb.GetEvidenceValues();
        }
        catch (JsonSerializationException e)
        {
            logger.LogError(e, "Unable to parse bank endpoints response: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUnableToParseResponse, e.Message, e);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to fetch bank endpoints: {message}", e.Message);
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
            var endpoints = await controlInformationService.GetBankEndpointsWithDates();
            var json = JsonConvert.SerializeObject(endpoints);
            ecb.AddEvidenceValue(PluginConstants.DefaultValue, json, PluginConstants.SourceName, false);
            return ecb.GetEvidenceValues();
        }
        catch (JsonSerializationException e)
        {
            logger.LogError(e, "Unable to parse bank endpoints response: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUnableToParseResponse, e.Message, e);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to fetch bank endpoints: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, e.Message, e);
        }
    }
}
