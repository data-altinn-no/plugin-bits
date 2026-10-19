using System;
using Microsoft.Extensions.Hosting;
using Dan.Common.Extensions;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Services;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureDanPluginDefaults()
    .ConfigureAppConfiguration((_, _) =>
    {
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<Settings>(context.Configuration);
        services.AddHttpClient();
        services.AddSingleton<IMemoryCacheProvider, MemoryCacheProvider>();
        // Singletons: GithubClient holds the resolved PAT, ControlInformationService holds the single-flight locks.
        services.AddSingleton<IGithubClient, GithubClient>();
        services.AddSingleton<IControlInformationService, ControlInformationService>();
        services.AddSingleton(TimeProvider.System);
        // Warms the caches in every instance at start-up (deploy, restart, scale-out).
        services.AddHostedService<CacheWarmupService>();
    })
    .Build();

await host.RunAsync();
