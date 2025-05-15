using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Enums;
using Dan.Common.Interfaces;
using Dan.Common.Models;
using Dan.Plugin.DATASOURCENAME.Config;
using Dan.Plugin.DATASOURCENAME.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json.Schema.Generation;
using NJsonSchema;

namespace Dan.Plugin.DATASOURCENAME;

/// <summary>
/// All plugins must implement IEvidenceSourceMetadata, which describes that datasets returned by this plugin. An example is implemented below.
/// </summary>
public class Metadata : IEvidenceSourceMetadata
{
    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public List<EvidenceCode> GetEvidenceCodes()
    {
        return
        [
            new EvidenceCode
            {
                EvidenceCodeName = PluginConstants.SimpleDatasetName,
                EvidenceSource = PluginConstants.SourceName,
                Values =
                [
                    new EvidenceValue
                    {
                        EvidenceValueName = "field1",
                        ValueType = EvidenceValueType.String
                    },

                    new EvidenceValue
                    {
                        EvidenceValueName = "field2",
                        ValueType = EvidenceValueType.String
                    }
                ]
            },
            new EvidenceCode
            {
                EvidenceCodeName = PluginConstants.RichDatasetName,
                EvidenceSource = PluginConstants.SourceName,
                Values =
                [
                    new EvidenceValue
                    {
                        // Convention for rich datasets with a single JSON model is to use the value name "default"
                        EvidenceValueName = "default",
                        ValueType = EvidenceValueType.JsonSchema,
                        JsonSchemaDefintion = JsonSchema
                            .FromType<ExampleModel>()
                            .ToJson(Newtonsoft.Json.Formatting.Indented)
                    }
                ],
                AuthorizationRequirements =
                [
                    new MaskinportenScopeRequirement
                    {
                        RequiredScopes = ["altinn:dataaltinnno/somescope"]
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
