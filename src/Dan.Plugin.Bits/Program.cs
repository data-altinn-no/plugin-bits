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
        services.AddTransient<IMemoryCacheProvider, MemoryCacheProvider>();
        services.AddTransient<IControlInformationService, ControlInformationService>();
    })
    .Build();

await host.RunAsync();
