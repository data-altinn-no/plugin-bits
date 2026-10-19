using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using Dan.Plugin.Bits.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Dan.Plugin.Bits.Test;

/// <summary>
/// Pins the JSON the datasets return (property names, which fields are present) so cache changes cannot alter
/// what clients receive. Serialization here mirrors Plugin.cs: JsonConvert.SerializeObject of the service result.
/// </summary>
public class ClientContractTests
{
    private const string Csv =
        "orgnummer,navn,url,versjon,fromdate,todate\n" +
        "111111111,Bank A,https://a.example/api,v1,,\n" +
        "222222222,Bank B,https://b.example/api,v2,2024-01-01T00:00:00Z,2099-12-31T00:00:00Z\n" +
        "333333333,Bank Closed,https://c.example/api,v1,,2020-01-01T00:00:00Z\n";

    private const string LimitationJson =
        "[{\"limitationId\":\"LIM-1\",\"limitationGroupId\":\"G1\",\"area\":\"Konto\",\"apiResponseStatus\":\"Partial\"," +
        "\"description\":\"desc\",\"functionalDescription\":\"func\",\"guidance\":\"guide\",\"plannedFixDate\":\"2026-12-01\",\"updatedAt\":\"2026-10-01\"}]";

    private static byte[] Zip()
    {
        var files = new Dictionary<string, string>
        {
            ["test/Banker_Kontroll_test.csv"] = Csv,
            ["test/Banker_Pantutlegg_test.csv"] = Csv,
            ["test/limitations/111111111.json"] = LimitationJson
        };

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry($"data-altinn-no-bits-abc/{path}").Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static ControlInformationService CreateService()
    {
        var github = Substitute.For<IGithubClient>();
        github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip()));
        var settings = new Settings
        {
            EndpointsResourceFile = "test/Banker_Kontroll_test.csv",
            PantUtleggResourceFile = "test/Banker_Pantutlegg_test.csv",
            LimitationsResourceFile = "test/limitations/",
            UseTestEndpoints = true
        };
        return new ControlInformationService(github, NullLoggerFactory.Instance, Options.Create(settings),
            new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions())), TimeProvider.System);
    }

    private static List<string> Keys(JToken token) => ((JObject)token).Properties().Select(p => p.Name).ToList();

    [Fact]
    public async Task Kontrollinformasjon_json_has_only_the_five_basic_fields()
    {
        var json = JArray.Parse(JsonConvert.SerializeObject(await CreateService().GetBankEndpoints()));

        Assert.Equal(2, json.Count);
        Assert.All(json, endpoint => Assert.Equal(["orgNo", "name", "url", "version", "env"], Keys(endpoint)));
        Assert.Equal("111111111", json[0]["orgNo"]);
        Assert.Equal("Bank A", json[0]["name"]);
        Assert.Equal("https://a.example/api", json[0]["url"]);
        Assert.Equal("v1", json[0]["version"]);
        Assert.Equal("test", json[0]["env"]);
    }

    [Fact]
    public async Task KontrollinformasjonUtvidet_json_adds_dates_when_set_and_always_limitations()
    {
        var json = JArray.Parse(JsonConvert.SerializeObject(await CreateService().GetBankEndpointsWithDates()));

        Assert.Equal(2, json.Count);
        // No dates in the CSV: fromDate/toDate are omitted, limitations is present.
        Assert.Equal(["orgNo", "name", "url", "version", "env", "limitations"], Keys(json[0]));
        // Dates in the CSV: both are present and serialized as ISO 8601 with offset.
        Assert.Equal(["orgNo", "name", "url", "version", "env", "fromDate", "toDate", "limitations"], Keys(json[1]));
        Assert.Equal("2024-01-01T00:00:00+00:00", json[1]["fromDate"]);
        Assert.Equal("2099-12-31T00:00:00+00:00", json[1]["toDate"]);
        Assert.Empty(json[1]["limitations"]!);

        var limitation = json[0]["limitations"]!.Single();
        Assert.Equal(
            ["limitationId", "limitationGroupId", "area", "apiResponseStatus", "description", "functionalDescription", "guidance", "plannedFixDate", "updatedAt"],
            Keys(limitation));
        Assert.Equal("LIM-1", limitation["limitationId"]);
    }

    [Fact]
    public async Task Begrensninger_json_is_a_limitations_list_with_total()
    {
        var service = CreateService();

        var known = await service.GetBankLimitations("111111111");
        var knownJson = JObject.Parse(JsonConvert.SerializeObject(new LimitationsList { Limitations = known, Total = known.Count }));
        Assert.Equal(["limitations", "total"], Keys(knownJson));
        Assert.Equal(1, knownJson["total"]);
        Assert.Equal("LIM-1", knownJson["limitations"]![0]!["limitationId"]);

        var unknown = await service.GetBankLimitations("999999999");
        var unknownJson = JObject.Parse(JsonConvert.SerializeObject(new LimitationsList { Limitations = unknown, Total = unknown.Count }));
        Assert.Equal(0, unknownJson["total"]);
        Assert.Empty(unknownJson["limitations"]!);
    }

    [Fact]
    public async Task PantUtlegg_json_has_basic_fields_and_dates_when_set()
    {
        var json = JArray.Parse(JsonConvert.SerializeObject(await CreateService().GetPantUtlegg()));

        // PantUtlegg returns the CSV as-is, including inactive rows and dates, never limitations.
        Assert.Equal(3, json.Count);
        Assert.Equal(["orgNo", "name", "url", "version", "env"], Keys(json[0]));
        Assert.Equal(["orgNo", "name", "url", "version", "env", "fromDate", "toDate"], Keys(json[1]));
        Assert.Equal(["orgNo", "name", "url", "version", "env", "toDate"], Keys(json[2]));
    }
}
