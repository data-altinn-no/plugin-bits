using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dan.Plugin.Bits.Services;

/// <summary>
/// Builds the caches when the worker starts, so a deploy, restart or scale-out does not leave the first client
/// request to do it. Runs in every instance (unlike the timer, which is a singleton across instances) and does not
/// block start-up: a request arriving mid-warm-up waits on the service's single-flight lock and gets the fresh data.
/// A failed warm-up is logged; the first request or the timer rebuilds as before.
/// </summary>
public class CacheWarmupService(IControlInformationService controlInformationService, ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly ILogger logger = loggerFactory.CreateLogger<CacheWarmupService>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the host finish starting before the Key Vault and GitHub round trips
        logger.LogInformation("Cache warm-up started");

        try
        {
            await controlInformationService.RefreshAll();
            logger.LogInformation("Cache warm-up completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cache warm-up failed, the cache will be built on first request or by the scheduled refresh: {message}", ex.Message);
        }
    }
}
