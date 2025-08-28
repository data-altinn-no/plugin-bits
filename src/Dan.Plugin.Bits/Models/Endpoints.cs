using System.Collections.Generic;
using FileHelpers;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits.Models;

[DelimitedRecord(",")]
[IgnoreFirst]
public class EndpointV2
{
    public string OrgNummer { get; set; }

    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string Navn { get; set; }

    public string Url { get; set; }

    public string Version { get; set; }
}

public class EndpointsList
{
    [JsonProperty("endpoints")]
    public List<EndpointExternal> Endpoints { get; set; }

    [JsonProperty("total")]
    public int Total { get; set; }
}

public class EndpointExternal
{
    [JsonProperty("orgNo")]
    public string OrgNo { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("env")]
    public string Env { get; set; }
}
