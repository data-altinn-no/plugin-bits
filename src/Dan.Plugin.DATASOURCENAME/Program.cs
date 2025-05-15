using Microsoft.Extensions.Hosting;
using Dan.Common.Extensions;
using Dan.Plugin.DATASOURCENAME.Config;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureDanPluginDefaults()
    .ConfigureAppConfiguration((_, _) =>
    {
        // Add more configuration sources if necessary. ConfigureDanPluginDefaults will load environment variables, which includes
        // local.settings.json (if developing locally) and applications settings for the Azure Function
    })
    .ConfigureServices((context, services) =>
    {
        // Add any additional services here

        // This makes IOption<Settings> available in the DI container.
        var configurationRoot = context.Configuration;
        services.Configure<Settings>(configurationRoot);
    })
    .Build();

await host.RunAsync();
