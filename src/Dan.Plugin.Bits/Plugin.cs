using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Dan.Common.Exceptions;
using Dan.Common.Models;
using Dan.Common.Util;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using Dan.Plugin.Bits.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Dan.Plugin.Bits;

public class Plugin
{
    private readonly ILogger _logger;
    private readonly IControlInformationService _controlInformationService;
    private static readonly Regex NorwegianOrganizationNumberPattern = new(@"\A[0-9]{9}\z", RegexOptions.Compiled);

    /// <summary>
    /// Safety net for the GitHub-triggered refresh (missed webhook, failed workflow run, recycled instance).
    /// Runs at the top of every hour.
    /// </summary>
    private const string CacheRefreshSchedule = "0 0 * * * *";

    public Plugin(IControlInformationService controlInformationService, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Plugin>();
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
            // Limitations are attached to the endpoints when the cached snapshot is built - see ControlInformationService.
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

    [Function(PluginConstants.Begrensninger)]
    public async Task<HttpResponseData> GetBegrensninger(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext context)
    {
        return await EvidenceSourceResponse.CreateResponse(req, () => GetEvidenceValuesBegrensninger(req));
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesBegrensninger(HttpRequestData req)
    {
        try
        {
            var evidenceHarvesterRequest = await req.ReadFromJsonAsync<EvidenceHarvesterRequest>();
            var orgNo = evidenceHarvesterRequest?.SubjectParty?.NorwegianOrganizationNumber;

            if (string.IsNullOrWhiteSpace(orgNo) || !NorwegianOrganizationNumberPattern.IsMatch(orgNo))
            {
                throw new EvidenceSourcePermanentClientException(PluginConstants.ErrorInvalidInput, "Organisasjonsnummer mangler eller er ugyldig i forespørselen");
            }

            var ecb = new EvidenceBuilder(new Metadata(), PluginConstants.Begrensninger);
            var limitations = await _controlInformationService.GetBankLimitations(orgNo);
            var json = JsonConvert.SerializeObject(new LimitationsList { Limitations = limitations, Total = limitations.Count });
            ecb.AddEvidenceValue(PluginConstants.DefaultValue, json, PluginConstants.SourceName, false);
            return ecb.GetEvidenceValues();
        }
        catch (JsonSerializationException e)
        {
            _logger.LogError(e, "Unable to parse limitations response: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUnableToParseResponse, e.Message, e);
        }
        catch (EvidenceSourceException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to fetch limitations: {message}", e.Message);
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, e.Message, e);
        }
    }

    [Function(PluginConstants.PantUtlegg)]
    public async Task<HttpResponseData> PantUtlegg(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
    FunctionContext context)
    {
        return await EvidenceSourceResponse.CreateResponse(req, GetEvidenceValuesPantUtlegg);
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesPantUtlegg()
    {
        try
        {
            var ecb = new EvidenceBuilder(new Metadata(), PluginConstants.PantUtlegg);
            var endpoints = await _controlInformationService.GetPantUtlegg();
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

    /// <summary>
    /// Called by the bits repo workflow on changes to the control CSV or any limitation file.
    /// Re-reads everything from GitHub and swaps the cached snapshot. Fails with 500 (and keeps the old snapshot)
    /// if anything cannot be fetched, so the workflow run shows the failure.
    /// </summary>
    [Function("OppdaterKontrollinformasjon")]
    public async Task<HttpResponseData> UpdateKontrollinformasjon(
           [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req,
           FunctionContext context)
    {
        _logger.LogInformation("Cache refresh requested via webhook OppdaterKontrollinformasjon");
        var endpoints = await _controlInformationService.RefreshControlInformation();
        _logger.LogInformation("Webhook OppdaterKontrollinformasjon completed - returning {endpointCount} endpoints", endpoints.Count);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new EndpointsList() { Endpoints = endpoints, Total = endpoints.Count });
        return response;
    }

    /// <summary>
    /// Called by the bits repo workflow on changes to the PantUtlegg CSV.
    /// </summary>
    [Function("OppdaterPantUtlegg")]
    public async Task<HttpResponseData> OppdaterPantUtlegg(
       [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req,
       FunctionContext context)
    {
        _logger.LogInformation("Cache refresh requested via webhook OppdaterPantUtlegg");
        var endpoints = await _controlInformationService.RefreshPantUtlegg();
        _logger.LogInformation("Webhook OppdaterPantUtlegg completed - returning {endpointCount} endpoints", endpoints.Count);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new EndpointsList() { Endpoints = endpoints, Total = endpoints.Count });
        return response;
    }

    /// <summary>
    /// Scheduled safety net. One download of the repository refreshes both caches; a failure surfaces as a failed run
    /// and keeps the previous data.
    /// </summary>
    [Function("OppdaterCachePlanlagt")]
    public async Task ScheduledCacheRefresh(
        [TimerTrigger(CacheRefreshSchedule)] TimerInfo timer,
        FunctionContext context)
    {
        _logger.LogInformation("Cache refresh requested via timer OppdaterCachePlanlagt (past due: {isPastDue}, next run: {next})", timer.IsPastDue, timer.ScheduleStatus?.Next);
        await _controlInformationService.RefreshAll();
        _logger.LogInformation("Scheduled cache refresh completed");
    }
}
