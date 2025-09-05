using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Enums;
using Dan.Common.Interfaces;
using Dan.Common.Models;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits;

/// <summary>
/// All plugins must implement IEvidenceSourceMetadata, which describes that datasets returned by this plugin. An example is implemented below.
/// </summary>
public class Metadata : IEvidenceSourceMetadata
{
    private const string ServiceContext = "Bits kontrollinformasjon";
    private const string Scope = "altinn:dataaltinnno/kontrollinformasjon";
    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public List<EvidenceCode> GetEvidenceCodes()
    {
        return
        [
            new EvidenceCode()
            {
                EvidenceCodeName = PluginConstants.Kontrollinformasjon,
                EvidenceSource = PluginConstants.SourceName,
                BelongsToServiceContexts = [ServiceContext],
                Values =
                [
                    new EvidenceValue()
                    {
                        EvidenceValueName = PluginConstants.DefaultValue,
                        ValueType = EvidenceValueType.JsonSchema,
                        JsonSchemaDefintion = EvidenceValue.SchemaFromObject<EndpointsList>(Formatting.Indented)
                    }
                ],
                AuthorizationRequirements =
                [
                    new MaskinportenScopeRequirement()
                    {
                        RequiredScopes = [Scope]
                    }
                ]
            },
            new EvidenceCode()
            {
                EvidenceCodeName = PluginConstants.KontrollinformasjonV2,
                EvidenceSource = PluginConstants.SourceName,
                BelongsToServiceContexts = [ServiceContext],
                Values =
                [
                    new EvidenceValue()
                    {
                        EvidenceValueName = PluginConstants.DefaultValue,
                        ValueType = EvidenceValueType.JsonSchema,
                        JsonSchemaDefintion = EvidenceValue.SchemaFromObject<EndpointsList>(Formatting.Indented)
                    }
                ],
                AuthorizationRequirements =
                [
                    new MaskinportenScopeRequirement()
                    {
                        RequiredScopes = [Scope]
                    }
                ]
            }
        ];
    }


    /// <summary>
    /// This function must be defined in all DAN plugins, and is used by core to enumerate the available datasets across all plugins.
    /// Normally this should not be changed.
    /// </summary>
    /// <param name="req"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    [Function(Constants.EvidenceSourceMetadataFunctionName)]
    public async Task<HttpResponseData> GetMetadataAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req,
        FunctionContext context)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(GetEvidenceCodes());
        return response;
    }

}
