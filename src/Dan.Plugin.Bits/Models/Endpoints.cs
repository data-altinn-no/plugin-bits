using System;
using System.Collections.Generic;
using FileHelpers;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits.Models;

[DelimitedRecord(",")]
[IgnoreFirst]
[IgnoreEmptyLines]
public class EndpointV2
{
    public string OrgNummer { get; set; }

    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string Navn { get; set; }

    public string Url { get; set; }

    public string Version { get; set; }

    [FieldQuoted(QuoteMode.OptionalForBoth)]
    [FieldOptional]
    [FieldConverter(typeof(NullableDateTimeOffsetConverter))]
    public DateTimeOffset? FromDate { get; set; }

    [FieldQuoted(QuoteMode.OptionalForBoth)]
    [FieldOptional]
    [FieldConverter(typeof(NullableDateTimeOffsetConverter))]
    public DateTimeOffset? ToDate { get; set; }
}

public class EndpointsList
{
    [JsonProperty("endpoints")]
    public IReadOnlyList<EndpointExternal> Endpoints { get; set; }

    [JsonProperty("total")]
    public int Total { get; set; }
}

public class LimitationsList
{
    [JsonProperty("limitations")]
    public IReadOnlyList<Limitation> Limitations { get; set; }

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

    [JsonProperty("fromDate", NullValueHandling=NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public DateTimeOffset? FromDate { get; set; }

    [JsonProperty("toDate", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public DateTimeOffset? ToDate { get; set; }

    [JsonProperty("limitations", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<Limitation> Limitations { get; set; }
}

public class Limitation
{
    [JsonProperty("limitationId")]
    public string LimitationId { get; set; }
    [JsonProperty("limitationGroupId")]
    public string? LimitationGroupId { get; set; } = null;
    [JsonProperty("area")]
    public string Area{ get; set; }
    [JsonProperty("apiResponseStatus")]
    [JsonConverter(typeof(ApiResponseStatusConverter))]
    public ApiResponseStatus? ApiResponseStatus { get; set; } = null;
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("guidence")]
    public string Guidence { get; set; } = null;
    [JsonProperty("plannedFixDate")]
    public DateOnly? PlannedFixDate { get; set; } = null;
    /// <summary>
    /// updatedAt angir når den konkrete begrensningen sist ble faglig oppdatert i grunnlagsdataene
    /// </summary>
    [JsonProperty("updatedAt")]
    public DateOnly? UpdatedAt { get; set; }
}

public enum ApiResponseStatus
{
    Partial,
    Complete,
    DataNotDelivered,
    NotApplicable,
    Unknown
}
