using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dan.Common.Exceptions;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dan.Plugin.Bits.Test;

public class ControlInformationServiceTests
{
    private const string EndpointsFile = "test/Banker_Kontroll_test.csv";
    private const string PantUtleggFile = "test/Banker_Pantutlegg_test.csv";
    private const string LimitationsDir = "test/limitations/";

    private const string CsvHeader = "orgnummer,navn,url,versjon,fromdate,todate\n";

    private const string EndpointsCsv = CsvHeader +
        "111111111,Bank A,https://a.example/api,v1,,\n" +
        "222222222,\"Bank B, AS\",https://b.example/api,v1,,\n" +
        "333333333,Bank Closed,https://c.example/api,v1,,2020-01-01T00:00:00Z\n" +
        "444444444,Bank Future,https://d.example/api,v1,2099-01-01T00:00:00Z,\n";

    private const string PantUtleggCsv = CsvHeader + "555555555,Pant Bank,https://p.example/api,v1,,\n";

    private static string LimitationJson(string id) =>
        $"[{{\"limitationId\":\"{id}\",\"description\":\"desc\",\"updatedAt\":\"2026-10-01\"}}]";

    private static readonly Dictionary<string, string> DefaultRepo = new()
    {
        ["README.md"] = "# bits",
        [EndpointsFile] = EndpointsCsv,
        [PantUtleggFile] = PantUtleggCsv,
        ["test/.limitations-export-manifest.json"] = "[]",
        ["test/limitations/111111111.json"] = LimitationJson("L1"),
        ["test/limitations/999999999.json"] = LimitationJson("L9"),
        ["test/limitations/.limitations-export-manifest.json"] = "[\"111111111.json\"]",
        ["test/limitations/notes.md"] = "not a limitation",
        ["test/limitations/archive/777777777.json"] = LimitationJson("nested-should-be-ignored"),
        ["prod/limitations/111111111.json"] = LimitationJson("other-env-should-be-ignored")
    };

    /// <summary>
    /// Builds a zip shaped like a GitHub zipball: one root folder named after the commit, files below it.
    /// </summary>
    private static byte[] Zip(IDictionary<string, string> files, string root = "data-altinn-no-bits-1a2b3c4")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry(root + "/");
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry($"{root}/{path}").Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Zip(Action<Dictionary<string, string>> modify)
    {
        var files = new Dictionary<string, string>(DefaultRepo);
        modify(files);
        return Zip(files);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private readonly IGithubClient _github = Substitute.For<IGithubClient>();
    private readonly FakeTimeProvider _clock = new();

    public ControlInformationServiceTests()
    {
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(DefaultRepo)));
    }

    private ControlInformationService CreateService(string? limitationsDir = LimitationsDir, int softTtlMinutes = 60)
    {
        var settings = new Settings
        {
            EndpointsResourceFile = EndpointsFile,
            PantUtleggResourceFile = PantUtleggFile,
            LimitationsResourceFile = limitationsDir,
            UseTestEndpoints = false,
            CacheSoftTtlMinutes = softTtlMinutes
        };

        var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()));
        return new ControlInformationService(_github, NullLoggerFactory.Instance, Options.Create(settings), cache, _clock);
    }

    [Fact]
    public async Task GetBankEndpoints_returns_only_active_endpoints_without_dates_or_limitations()
    {
        var service = CreateService();

        var endpoints = await service.GetBankEndpoints();

        Assert.Equal(["111111111", "222222222"], endpoints.Select(e => e.OrgNo));
        Assert.All(endpoints, e =>
        {
            Assert.Null(e.FromDate);
            Assert.Null(e.ToDate);
            Assert.Null(e.Limitations);
            Assert.Equal("prod", e.Env);
        });
        Assert.Equal("Bank B, AS", endpoints[1].Name);
    }

    [Fact]
    public async Task GetBankEndpointsWithDates_attaches_limitations_from_snapshot()
    {
        var service = CreateService();

        var endpoints = await service.GetBankEndpointsWithDates();

        Assert.Equal(["111111111", "222222222"], endpoints.Select(e => e.OrgNo));
        Assert.Equal("L1", Assert.Single(endpoints[0].Limitations).LimitationId);
        Assert.Empty(endpoints[1].Limitations);
    }

    [Fact]
    public async Task Only_json_files_directly_in_the_limitations_folder_are_read()
    {
        var service = CreateService();

        await service.GetBankEndpoints();

        Assert.Equal("L9", Assert.Single(await service.GetBankLimitations("999999999")).LimitationId);
        Assert.Empty(await service.GetBankLimitations("777777777"));
        Assert.Empty(await service.GetBankLimitations("notes"));
        Assert.Empty(await service.GetBankLimitations(".limitations-export-manifest"));
    }

    [Fact]
    public async Task GetBankLimitations_for_unknown_org_returns_empty_without_touching_github_again()
    {
        var service = CreateService();
        await service.GetBankLimitations("999999999");
        _github.ClearReceivedCalls();

        var unknown = await service.GetBankLimitations("123456789");

        Assert.Empty(unknown);
        await _github.DidNotReceive().DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Concurrent_cold_requests_download_the_archive_only_once()
    {
        var service = CreateService();

        var requests = Enumerable.Range(0, 25).Select(i => (i % 3) switch
        {
            0 => service.GetBankEndpoints().ContinueWith(_ => { }),
            1 => service.GetBankEndpointsWithDates().ContinueWith(_ => { }),
            _ => service.GetBankLimitations("111111111").ContinueWith(_ => { })
        });
        await Task.WhenAll(requests);

        await _github.Received(1).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Cached_snapshot_is_served_without_touching_github()
    {
        var service = CreateService();
        await service.GetBankEndpoints();
        _github.ClearReceivedCalls();

        await service.GetBankEndpoints();
        await service.GetBankEndpointsWithDates();
        await service.GetBankLimitations("111111111");

        await _github.DidNotReceive().DownloadRepositoryArchive();
    }

    [Fact]
    public async Task RefreshControlInformation_replaces_cached_limitations_and_endpoints()
    {
        var service = CreateService();
        await service.GetBankEndpointsWithDates();

        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files =>
        {
            files["test/limitations/111111111.json"] = LimitationJson("L1-updated");
            files[EndpointsFile] = CsvHeader + "111111111,Bank A renamed,https://a.example/api,v2,,\n";
        })));

        var refreshed = await service.RefreshControlInformation();

        Assert.Equal("Bank A renamed", Assert.Single(refreshed).Name);
        Assert.Equal("L1-updated", Assert.Single(await service.GetBankLimitations("111111111")).LimitationId);
        Assert.Equal("L1-updated", Assert.Single((await service.GetBankEndpointsWithDates()).Single().Limitations).LimitationId);
    }

    [Fact]
    public async Task Failed_download_keeps_the_previous_snapshot()
    {
        var service = CreateService();
        var before = await service.GetBankEndpoints();

        _github.DownloadRepositoryArchive().ThrowsAsync(new HttpRequestException("github down"));

        await Assert.ThrowsAsync<EvidenceSourceTransientException>(() => service.RefreshControlInformation());

        var after = await service.GetBankEndpoints();
        Assert.Equal(before.Select(e => e.OrgNo), after.Select(e => e.OrgNo));
        Assert.Equal("L1", Assert.Single(await service.GetBankLimitations("111111111")).LimitationId);
    }

    [Fact]
    public async Task Missing_endpoints_file_in_archive_fails_the_rebuild()
    {
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files => files.Remove(EndpointsFile))));
        var service = CreateService();

        await Assert.ThrowsAsync<EvidenceSourceTransientException>(() => service.GetBankEndpoints());
    }

    [Fact]
    public async Task Empty_endpoints_csv_fails_the_rebuild()
    {
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files => files[EndpointsFile] = CsvHeader)));
        var service = CreateService();

        await Assert.ThrowsAsync<EvidenceSourceTransientException>(() => service.GetBankEndpoints());
    }

    [Fact]
    public async Task Header_only_pantutlegg_csv_is_a_valid_empty_list()
    {
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files => files[PantUtleggFile] = CsvHeader)));
        var service = CreateService();

        Assert.Empty(await service.GetPantUtlegg());
        Assert.Empty(await service.RefreshPantUtlegg());
        await service.RefreshAll();
        await _github.Received(3).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Missing_limitations_directory_setting_builds_snapshot_without_limitations()
    {
        var service = CreateService(limitationsDir: null);

        var endpoints = await service.GetBankEndpointsWithDates();

        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, e => Assert.Empty(e.Limitations));
    }

    [Fact]
    public async Task PantUtlegg_is_built_once_and_refreshed_on_demand()
    {
        var service = CreateService();

        var first = await service.GetPantUtlegg();
        var second = await service.GetPantUtlegg();
        await _github.Received(1).DownloadRepositoryArchive();
        Assert.Equal("555555555", Assert.Single(first).OrgNo);
        Assert.Same(first, second);

        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files =>
            files[PantUtleggFile] = CsvHeader + "666666666,Other Pant Bank,https://q.example/api,v1,,\n")));

        var refreshed = await service.RefreshPantUtlegg();
        Assert.Equal("666666666", Assert.Single(refreshed).OrgNo);
        Assert.Equal("666666666", Assert.Single(await service.GetPantUtlegg()).OrgNo);
    }

    [Fact]
    public async Task RefreshAll_downloads_once_and_refreshes_both_caches()
    {
        var service = CreateService();

        await service.RefreshAll();

        await _github.Received(1).DownloadRepositoryArchive();
        _github.ClearReceivedCalls();
        Assert.Equal(2, (await service.GetBankEndpoints()).Count);
        Assert.Single(await service.GetPantUtlegg());
        await _github.DidNotReceive().DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Fresh_snapshot_does_not_trigger_a_background_refresh()
    {
        var service = CreateService(softTtlMinutes: 60);
        await service.GetBankEndpoints();
        _clock.Advance(TimeSpan.FromMinutes(59));

        await service.GetBankEndpoints();
        await service.ControlInformationBackgroundRefresh;

        await _github.Received(1).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Stale_snapshot_is_served_immediately_and_refreshed_in_the_background()
    {
        var service = CreateService(softTtlMinutes: 60);
        await service.GetBankEndpointsWithDates();
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files =>
            files["test/limitations/111111111.json"] = LimitationJson("L1-updated"))));
        _clock.Advance(TimeSpan.FromMinutes(61));

        // Many stale requests at once: all answered from the old snapshot, exactly one background download.
        var served = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetBankLimitations("111111111")));
        await service.ControlInformationBackgroundRefresh;

        Assert.All(served, limitations => Assert.Equal("L1", Assert.Single(limitations).LimitationId));
        await _github.Received(2).DownloadRepositoryArchive();
        Assert.Equal("L1-updated", Assert.Single(await service.GetBankLimitations("111111111")).LimitationId);

        // The refreshed snapshot is fresh again: no further download.
        await service.GetBankEndpoints();
        await service.ControlInformationBackgroundRefresh;
        await _github.Received(2).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Failed_background_refresh_keeps_old_data_and_is_retried_on_the_next_stale_request()
    {
        var service = CreateService(softTtlMinutes: 60);
        await service.GetBankEndpoints();
        _clock.Advance(TimeSpan.FromMinutes(61));
        _github.DownloadRepositoryArchive().ThrowsAsync(new HttpRequestException("github down"));

        var served = await service.GetBankEndpoints();
        await service.ControlInformationBackgroundRefresh;

        Assert.Equal(2, served.Count);
        Assert.Equal(2, (await service.GetBankEndpoints()).Count);
        await service.ControlInformationBackgroundRefresh;
        await _github.Received(3).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Soft_ttl_zero_disables_background_refresh()
    {
        var service = CreateService(softTtlMinutes: 0);
        await service.GetBankEndpoints();
        _clock.Advance(TimeSpan.FromDays(30));

        await service.GetBankEndpoints();
        await service.ControlInformationBackgroundRefresh;

        await _github.Received(1).DownloadRepositoryArchive();
    }

    [Fact]
    public async Task Stale_pantutlegg_is_refreshed_in_the_background()
    {
        var service = CreateService(softTtlMinutes: 60);
        await service.GetPantUtlegg();
        _github.DownloadRepositoryArchive().Returns(Task.FromResult(Zip(files =>
            files[PantUtleggFile] = CsvHeader + "666666666,Other Pant Bank,https://q.example/api,v1,,\n")));
        _clock.Advance(TimeSpan.FromMinutes(61));

        var served = await service.GetPantUtlegg();
        await service.PantUtleggBackgroundRefresh;

        Assert.Equal("555555555", Assert.Single(served).OrgNo);
        Assert.Equal("666666666", Assert.Single(await service.GetPantUtlegg()).OrgNo);
        await _github.Received(2).DownloadRepositoryArchive();
    }

    [Fact]
    public void RepositoryArchive_strips_root_folder_and_lists_direct_children()
    {
        var archive = RepositoryArchive.FromZip(Zip(DefaultRepo));

        Assert.Equal("data-altinn-no-bits-1a2b3c4", archive.RootFolder);
        Assert.True(archive.TryGetFile("test/Banker_Kontroll_test.csv", out var csv));
        Assert.Equal(EndpointsCsv, csv);
        Assert.True(archive.TryGetFile("/TEST/banker_kontroll_test.csv", out _));
        Assert.False(archive.TryGetFile("test/missing.csv", out _));

        var names = archive.FilesInDirectory("test/limitations/").Select(f => f.Name).ToList();
        Assert.Equal([".limitations-export-manifest.json", "111111111.json", "999999999.json", "notes.md"], names);
    }
}
