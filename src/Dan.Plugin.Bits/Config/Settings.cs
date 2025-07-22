using System;
using Dan.Common.Config;

namespace Dan.Plugin.Bits.Config;

public class Settings
{
    public int DefaultCircuitBreakerOpenCircuitTimeSeconds { get; init; }
    public int DefaultCircuitBreakerFailureBeforeTripping { get; init; }
    public int SafeHttpClientTimeout { get; init; }

    public string EndpointUrl { get; init; }
    public bool UseTestEndpoints { get; init; }
    public string EndpointsResourceFile { get; init; }

    private static string KeyVaultName => Environment.GetEnvironmentVariable("KeyVaultName");
    private string GithubPatValue { get; init; }
    public string GithubPatName { get; init; }

    public string GithubPat
    {
        get => GithubPatValue ?? new PluginKeyVault(KeyVaultName).Get(GithubPatName).Result;
        init => GithubPatValue = value;
    }
}
