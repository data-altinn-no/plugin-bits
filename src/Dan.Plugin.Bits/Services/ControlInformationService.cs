using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dan.Common.Exceptions;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Models;
using FileHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Dan.Plugin.Bits.Services;

public interface IControlInformationService
{
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpoints();
    Task<IReadOnlyList<EndpointExternal>> GetBankEndpointsWithDates();
    Task<IReadOnlyList<EndpointExternal>> GetPantUtlegg();
    Task<IReadOnlyList<Limitation>> GetBankLimitations(string orgNo);

    /// <summary>
    /// Re-reads endpoints and all limitations from GitHub and replaces the cached snapshot. Called by the
    /// OppdaterKontrollinformasjon webhook (triggered from the bits repo).
    /// </summary>
    Task<IReadOnlyList<EndpointExternal>> RefreshControlInformation();

    /// <summary>
    /// Re-reads PantUtlegg endpoints from GitHub and replaces the cached list. Called by the OppdaterPantUtlegg webhook.
    /// </summary>
    Task<IReadOnlyList<EndpointExternal>> RefreshPantUtlegg();

    /// <summary>
    /// Refreshes everything from one download of the repository. Called at worker start-up and by the scheduled refresh.
    /// </summary>
    Task RefreshAll();
}

/// <summary>
/// Cache flow: every instance warms its cache at start-up, the bits repo calls the Oppdater* functions on every change,
/// and a timer refreshes as a safety net. On top of that each snapshot has a soft time-to-live: a request that finds a
/// snapshot older than <see cref="Settings.CacheSoftTtlMinutes"/> is answered from it immediately while one background
/// rebuild replaces it, so staleness is bounded per instance without ever putting GitHub on the request path.
/// A rebuild is one download of the repository archive; concurrent rebuilds are serialised by single-flight locks.
/// </summary>
public class ControlInformationService(
    IGithubClient github,
    ILoggerFactory loggerFactory,
    IOptions<Settings> settings,
    IMemoryCacheProvider memCache,
    TimeProvider timeProvider) : IControlInformationService
{
    private readonly ILogger logger = loggerFactory.CreateLogger<ControlInformationService>();
    private readonly Settings settings = settings.Value;

    // Always taken in this order (control information, then PantUtlegg) to avoid deadlocks in RefreshAll.
    private readonly SemaphoreSlim controlInformationGate = new(1, 1);
    private readonly SemaphoreSlim pantUtleggGate = new(1, 1);

    private readonly BackgroundRefresh controlInformationBackgroundRefresh = new(loggerFactory.CreateLogger<BackgroundRefresh>(), "control information");
    private readonly BackgroundRefresh pantUtleggBackgroundRefresh = new(loggerFactory.CreateLogger<BackgroundRefresh>(), "PantUtlegg");

    private const string ControlInformationKey = "control_information_snapshot";
    private const string PantUtleggKey = "pantutlegg_snapshot";
    private const int ErrorKeyTemp = 1;

    /// <summary>The background refresh currently running (or the last one), so tests can await it.</summary>
    internal Task ControlInformationBackgroundRefresh => controlInformationBackgroundRefresh.Current;

    /// <summary>The background refresh currently running (or the last one), so tests can await it.</summary>
    internal Task PantUtleggBackgroundRefresh => pantUtleggBackgroundRefresh.Current;

    public async Task<IReadOnlyList<EndpointExternal>> GetBankEndpoints()
    {
        var snapshot = await GetControlInformationSnapshot();

        // Inactive endpoints, dates and limitations are only returned in KontrollinformasjonUtvidet.
        var endpoints = snapshot.Endpoints
            .Where(IsActiveEndpoint)
            .Select(x => new EndpointExternal
            {
                Env = x.Env,
                OrgNo = x.OrgNo,
                Url = x.Url,
                Version = x.Version,
                Name = x.Name,
                ToDate = null,
                FromDate = null,
                Limitations = null
            })
            .ToList();

        logger.LogInformation("Returning total of {totalRecords} endpoints read from cache", endpoints.Count);
        return endpoints;
    }

    public async Task<IReadOnlyList<EndpointExternal>> GetBankEndpointsWithDates()
    {
        var snapshot = await GetControlInformationSnapshot();
        var endpoints = snapshot.Endpoints.Where(IsActiveEndpoint).ToList();

        logger.LogInformation("Returning total of {totalRecords} endpoints with limitations read from cache", endpoints.Count);
        return endpoints;
    }

    public async Task<IReadOnlyList<Limitation>> GetBankLimitations(string orgNo)
    {
        var snapshot = await GetControlInformationSnapshot();
        var limitations = snapshot.LimitationsByOrgNo.TryGetValue(orgNo, out var found) ? found : [];

        logger.LogInformation("Returning total of {totalRecords} limitations for {orgNo} read from cache", limitations.Count, orgNo);
        return limitations;
    }

    public async Task<IReadOnlyList<EndpointExternal>> GetPantUtlegg()
    {
        if (!memCache.TryGet(PantUtleggKey, out PantUtleggSnapshot snapshot))
        {
            logger.LogWarning("No PantUtlegg endpoints in cache - building on request path");
            await pantUtleggGate.WaitAsync();
            try
            {
                if (!memCache.TryGet(PantUtleggKey, out snapshot))
                {
                    snapshot = BuildAndStorePantUtlegg(await DownloadArchive());
                }
            }
            finally
            {
                pantUtleggGate.Release();
            }
        }
        else if (IsStale(snapshot.RefreshedAt))
        {
            pantUtleggBackgroundRefresh.TryStart(RefreshPantUtlegg, AgeOf(snapshot.RefreshedAt));
        }

        logger.LogInformation("Returning total of {totalRecords} PantUtlegg endpoints read from cache", snapshot.Endpoints.Count);
        return snapshot.Endpoints;
    }

    public async Task<IReadOnlyList<EndpointExternal>> RefreshControlInformation()
    {
        await controlInformationGate.WaitAsync();
        try
        {
            var snapshot = BuildAndStoreControlInformation(await DownloadArchive());
            return snapshot.Endpoints;
        }
        finally
        {
            controlInformationGate.Release();
        }
    }

    public async Task<IReadOnlyList<EndpointExternal>> RefreshPantUtlegg()
    {
        await pantUtleggGate.WaitAsync();
        try
        {
            return BuildAndStorePantUtlegg(await DownloadArchive()).Endpoints;
        }
        finally
        {
            pantUtleggGate.Release();
        }
    }

    public async Task RefreshAll()
    {
        await controlInformationGate.WaitAsync();
        try
        {
            await pantUtleggGate.WaitAsync();
            try
            {
                var archive = await DownloadArchive();
                BuildAndStoreControlInformation(archive);
                BuildAndStorePantUtlegg(archive);
            }
            finally
            {
                pantUtleggGate.Release();
            }
        }
        finally
        {
            controlInformationGate.Release();
        }
    }

    private async Task<ControlInformationSnapshot> GetControlInformationSnapshot()
    {
        if (memCache.TryGet(ControlInformationKey, out ControlInformationSnapshot snapshot))
        {
            if (IsStale(snapshot.RefreshedAt))
            {
                // Serve the current snapshot now; one background rebuild replaces it.
                controlInformationBackgroundRefresh.TryStart(RefreshControlInformation, AgeOf(snapshot.RefreshedAt));
            }

            return snapshot;
        }

        logger.LogWarning("No control information in cache - building snapshot on request path");

        // Single-flight: concurrent cold requests wait for the one rebuild instead of each hitting GitHub.
        await controlInformationGate.WaitAsync();
        try
        {
            if (memCache.TryGet(ControlInformationKey, out snapshot))
            {
                return snapshot;
            }

            return BuildAndStoreControlInformation(await DownloadArchive());
        }
        finally
        {
            controlInformationGate.Release();
        }
    }

    private TimeSpan AgeOf(DateTimeOffset refreshedAt) => timeProvider.GetUtcNow() - refreshedAt;

    private bool IsStale(DateTimeOffset refreshedAt)
    {
        return settings.CacheSoftTtlMinutes > 0
               && timeProvider.GetUtcNow() - refreshedAt > TimeSpan.FromMinutes(settings.CacheSoftTtlMinutes);
    }

    private async Task<RepositoryArchive> DownloadArchive()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var archive = RepositoryArchive.FromZip(await github.DownloadRepositoryArchive());
            logger.LogInformation("Repository archive {rootFolder} with {fileCount} files downloaded in {elapsedMs} ms",
                archive.RootFolder, archive.FileCount, stopwatch.ElapsedMilliseconds);
            return archive;
        }
        catch (Exception ex) when (ex is not EvidenceSourceException)
        {
            logger.LogCritical(ex, "Unable to download repository archive after {elapsedMs} ms: {message}", stopwatch.ElapsedMilliseconds, ex.Message);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, "Bank endpoints are currently unavailable", ex);
        }
    }

    /// <summary>
    /// Runs one rebuild step. Anything unexpected (malformed CSV or JSON, ...) is logged once with its root cause and
    /// surfaced as a transient evidence source error; errors that already are evidence source errors pass through.
    /// </summary>
    private T Rebuild<T>(string step, string unavailableMessage, Func<T> build)
    {
        try
        {
            return build();
        }
        catch (Exception ex) when (ex is not EvidenceSourceException)
        {
            logger.LogCritical(ex, "Unable to rebuild {step}: {message}", step, ex.Message);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, unavailableMessage, ex);
        }
    }

    private ControlInformationSnapshot BuildAndStoreControlInformation(RepositoryArchive archive) =>
        Rebuild("control information cache", "Bank endpoints are currently unavailable", () =>
        {
            var limitationsByOrgNo = ReadAllLimitations(archive);
            var endpoints = ReadEndpoints(archive, settings.EndpointsResourceFile, "Bank endpoints", allowEmpty: false);

            foreach (var endpoint in endpoints)
            {
                endpoint.Limitations = limitationsByOrgNo.TryGetValue(endpoint.OrgNo, out var found) ? found.ToList() : [];
            }

            memCache.TryGet(ControlInformationKey, out ControlInformationSnapshot previous);
            var snapshot = memCache.Set(ControlInformationKey, new ControlInformationSnapshot
            {
                Endpoints = endpoints,
                LimitationsByOrgNo = limitationsByOrgNo,
                RefreshedAt = timeProvider.GetUtcNow(),
                Source = archive.RootFolder
            });

            var totalLimitations = limitationsByOrgNo.Values.Sum(l => l.Count);
            if (previous == null)
            {
                logger.LogInformation(
                    "Control information cache built in this instance from {source}: {endpointCount} endpoints ({activeCount} active), {limitationCount} limitations for {limitationOrgCount} organisations",
                    snapshot.Source, endpoints.Count, endpoints.Count(IsActiveEndpoint), totalLimitations, limitationsByOrgNo.Count);
            }
            else
            {
                logger.LogInformation(
                    "Control information cache refreshed: {previousSource} -> {source} ({changed}); previous snapshot was {previousAgeMinutes:F0} min old; endpoints {previousEndpointCount} -> {endpointCount}, limitations {previousLimitationCount} -> {limitationCount} for {previousOrgCount} -> {limitationOrgCount} organisations",
                    previous.Source, snapshot.Source, previous.Source == snapshot.Source ? "same commit, data unchanged" : "new commit",
                    (snapshot.RefreshedAt - previous.RefreshedAt).TotalMinutes,
                    previous.Endpoints.Count, endpoints.Count,
                    previous.LimitationsByOrgNo.Values.Sum(l => l.Count), totalLimitations,
                    previous.LimitationsByOrgNo.Count, limitationsByOrgNo.Count);
            }

            return snapshot;
        });

    private PantUtleggSnapshot BuildAndStorePantUtlegg(RepositoryArchive archive) =>
        Rebuild("PantUtlegg cache", "PantUtlegg endpoints are currently unavailable", () =>
        {
            // PantUtlegg has no participating banks yet in prod, so a header-only CSV is a valid, empty list.
            var endpoints = ReadEndpoints(archive, settings.PantUtleggResourceFile, "PantUtlegg endpoints", allowEmpty: true);
            memCache.TryGet(PantUtleggKey, out PantUtleggSnapshot previous);
            var snapshot = memCache.Set(PantUtleggKey, new PantUtleggSnapshot
            {
                Endpoints = endpoints,
                RefreshedAt = timeProvider.GetUtcNow(),
                Source = archive.RootFolder
            });

            if (previous == null)
            {
                logger.LogInformation("PantUtlegg cache built in this instance from {source}: {endpointCount} endpoints", snapshot.Source, endpoints.Count);
            }
            else
            {
                logger.LogInformation(
                    "PantUtlegg cache refreshed: {previousSource} -> {source} ({changed}); previous snapshot was {previousAgeMinutes:F0} min old; endpoints {previousEndpointCount} -> {endpointCount}",
                    previous.Source, snapshot.Source, previous.Source == snapshot.Source ? "same commit, data unchanged" : "new commit",
                    (snapshot.RefreshedAt - previous.RefreshedAt).TotalMinutes, previous.Endpoints.Count, endpoints.Count);
            }

            return snapshot;
        });

    /// <summary>
    /// Parses an endpoints CSV from the archive. With <paramref name="allowEmpty"/> false, a CSV without data rows fails the
    /// rebuild so the previous snapshot is kept rather than replaced by an empty one.
    /// </summary>
    private List<EndpointExternal> ReadEndpoints(RepositoryArchive archive, string resourceFilePath, string resourceName, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(resourceFilePath) || !archive.TryGetFile(resourceFilePath, out var file))
        {
            logger.LogCritical("{resourceName} file {resourceFile} not found in repository archive {rootFolder}",
                resourceName, resourceFilePath, archive.RootFolder);
            throw new EvidenceSourceTransientException(ErrorKeyTemp, $"{resourceName} are currently unavailable");
        }

        var engine = new DelimitedFileEngine<EndpointV2>(Encoding.UTF8);
        var endpoints = engine.ReadString(file).ToList();

        logger.LogInformation("{resourceName} parsed from {resourceFile} csv - {totalRecords} records",
            resourceName, resourceFilePath, engine.TotalRecords);

        if (endpoints.Count == 0)
        {
            if (!allowEmpty)
            {
                logger.LogCritical("Plugin func-esbits no {resourceName} found in csv for {resourceFile}", resourceName, resourceFilePath);
                throw new EvidenceSourceTransientException(ErrorKeyTemp, $"{resourceName} are currently unavailable");
            }

            logger.LogWarning("No {resourceName} in csv {resourceFile} - caching an empty list", resourceName, resourceFilePath);
        }

        var env = settings.UseTestEndpoints ? "test" : "prod";
        return endpoints.Select(endpoint => new EndpointExternal
        {
            Env = env,
            Url = endpoint.Url,
            Name = endpoint.Navn,
            OrgNo = endpoint.OrgNummer,
            Version = endpoint.Version,
            ToDate = endpoint.ToDate,
            FromDate = endpoint.FromDate
        }).ToList();
    }

    /// <summary>
    /// Reads every "{orgNo}.json" in the limitations folder of the archive. Dot-files such as the export manifest are skipped.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyList<Limitation>> ReadAllLimitations(RepositoryArchive archive)
    {
        var result = new Dictionary<string, IReadOnlyList<Limitation>>();

        if (string.IsNullOrWhiteSpace(settings.LimitationsResourceFile))
        {
            logger.LogWarning("LimitationsResourceFile is not configured - no limitations will be cached");
            return result;
        }

        var files = archive.FilesInDirectory(settings.LimitationsResourceFile)
            .Where(file => file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !file.Name.StartsWith('.'))
            .ToList();

        foreach (var (name, content) in files)
        {
            result[Path.GetFileNameWithoutExtension(name)] = JsonConvert.DeserializeObject<List<Limitation>>(content) ?? [];
        }

        logger.LogInformation("Parsed {fileCount} limitation files from {directory}", files.Count, settings.LimitationsResourceFile);
        return result;
    }

    private bool IsActiveEndpoint(EndpointExternal x)
    {
        var now = timeProvider.GetUtcNow();
        return (x.FromDate == null || x.FromDate <= now) && (x.ToDate == null || x.ToDate >= now);
    }
}
