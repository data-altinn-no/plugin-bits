namespace Dan.Plugin.DATASOURCENAME.Config;

public class Settings
{
    public int DefaultCircuitBreakerOpenCircuitTimeSeconds { get; init; }
    public int DefaultCircuitBreakerFailureBeforeTripping { get; init; }
    public int SafeHttpClientTimeout { get; init; }

    public string EndpointUrl { get; init; }
}
