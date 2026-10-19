using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Config;
using Dan.Common.Exceptions;
using Dan.Plugin.Bits.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dan.Plugin.Bits.Services;

public interface IGithubClient
{
    /// <summary>
    /// Downloads the default branch of the data-altinn-no/bits repo as a zip archive. One request gives every
    /// CSV and limitation file from the same commit, instead of one request per file.
    /// </summary>
    Task<byte[]> DownloadRepositoryArchive();
}

/// <summary>
/// Thin wrapper around the GitHub API. Registered as a singleton so the PAT is resolved from Key Vault
/// once per process instead of on every request.
/// </summary>
public class GithubClient(
    IHttpClientFactory httpClientFactory,
    IOptions<Settings> settings,
    ILoggerFactory loggerFactory) : IGithubClient
{
    // GitHub answers with a 302 to codeload.github.com carrying a short-lived token in the URL. HttpClient follows
    // it and drops the Authorization header on the cross-host redirect, which is what GitHub expects.
    private const string ArchiveUrl = "https://api.github.com/repos/data-altinn-no/bits/zipball";
    private const int ErrorKeyTemp = 1;

    private readonly ILogger logger = loggerFactory.CreateLogger<GithubClient>();
    private readonly Settings settings = settings.Value;
    private readonly object tokenGate = new();
    private Task<string> tokenTask;

    public async Task<byte[]> DownloadRepositoryArchive()
    {
        var token = await GetToken();

        using var request = new HttpRequestMessage(HttpMethod.Get, ArchiveUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("plugin-bits");

        var client = httpClientFactory.CreateClient(Constants.SafeHttpClient);
        using var response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            logger.LogInformation("Downloaded repository archive from GitHub - {bytes} bytes", bytes.Length);
            return bytes;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // The PAT may have been rotated in Key Vault; make the next call resolve it again.
            lock (tokenGate)
            {
                tokenTask = null;
            }
        }

        logger.LogCritical("Github archive download failed, status code: {StatusCode}, reason: {ReasonPhrase}",
            response.StatusCode, response.ReasonPhrase);
        throw new EvidenceSourceTransientException(ErrorKeyTemp, "Banking endpoints are currently unavailable");
    }

    /// <summary>
    /// Resolves the PAT once and reuses it. A failed resolution is not cached, so a transient Key Vault
    /// error at startup is retried on the next call.
    /// </summary>
    private Task<string> GetToken()
    {
        lock (tokenGate)
        {
            if (tokenTask == null || tokenTask.IsFaulted || tokenTask.IsCanceled)
            {
                tokenTask = ResolveToken();
            }

            return tokenTask;
        }
    }

    private async Task<string> ResolveToken()
    {
        if (!string.IsNullOrEmpty(settings.GithubPat))
        {
            return settings.GithubPat;
        }

        // Locally DefaultAzureCredential can take a long time to probe credential sources; in Azure managed identity is fast.
        // Logged separately so a slow first rebuild is not mistaken for a slow GitHub download.
        var stopwatch = Stopwatch.StartNew();
        var pat = await new PluginKeyVault(Settings.KeyVaultName).Get(settings.GithubPatName);
        logger.LogInformation("Resolved GitHub PAT {secretName} from Key Vault in {elapsedMs} ms", settings.GithubPatName, stopwatch.ElapsedMilliseconds);
        return pat;
    }
}
