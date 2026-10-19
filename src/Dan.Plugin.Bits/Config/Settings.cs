using System;

namespace Dan.Plugin.Bits.Config;

public class Settings
{
    public int DefaultCircuitBreakerOpenCircuitTimeSeconds { get; init; }
    public int DefaultCircuitBreakerFailureBeforeTripping { get; init; }
    public int SafeHttpClientTimeout { get; init; }

    public string EndpointUrl { get; init; }
    public bool UseTestEndpoints { get; init; }

    /// <summary>
    /// Path (inside the data-altinn-no/bits repo) to the CSV with bank control endpoints, e.g. "prod/Banker_Kontroll_prod.csv".
    /// </summary>
    public string EndpointsResourceFile { get; init; }

    /// <summary>
    /// Path (inside the data-altinn-no/bits repo) to the CSV with PantUtlegg endpoints, e.g. "prod/Banker_Pantutlegg_prod.csv".
    /// </summary>
    public string PantUtleggResourceFile { get; init; }

    /// <summary>
    /// Directory (inside the data-altinn-no/bits repo) holding one "{orgNo}.json" file per bank, e.g. "prod/limitations/".
    /// </summary>
    public string LimitationsResourceFile { get; init; }

    /// <summary>
    /// Soft time-to-live for cached data. A request that finds data older than this is answered from it immediately
    /// while one background rebuild replaces it. Bounds staleness per instance when the webhook or timer hit another
    /// instance. 0 disables the background refresh.
    /// </summary>
    public int CacheSoftTtlMinutes { get; init; } = 60;

    public static string KeyVaultName => Environment.GetEnvironmentVariable("KeyVaultName");

    /// <summary>
    /// Name of the Key Vault secret holding the GitHub PAT. Used when <see cref="GithubPat"/> is not configured directly.
    /// </summary>
    public string GithubPatName { get; init; }

    /// <summary>
    /// GitHub PAT configured directly (local development). In Azure the PAT is resolved once from Key Vault
    /// by <see cref="Services.GithubClient"/> instead of on every request.
    /// </summary>
    public string GithubPat { get; init; }
}
