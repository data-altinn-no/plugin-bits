using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Dan.Plugin.Bits.Services;

/// <summary>
/// Runs at most one refresh at a time in the background. Used for the soft time-to-live: a request that finds a
/// stale snapshot is answered immediately and triggers this instead of waiting for GitHub.
/// A failed refresh is logged and the previous data is kept; the next stale request simply tries again.
/// </summary>
internal sealed class BackgroundRefresh(ILogger logger, string name)
{
    private readonly object gate = new();
    private Task current;

    /// <summary>The refresh currently running, or the last one. Lets tests await completion.</summary>
    public Task Current
    {
        get
        {
            lock (gate)
            {
                return current ?? Task.CompletedTask;
            }
        }
    }

    public void TryStart(Func<Task> refresh, TimeSpan age)
    {
        lock (gate)
        {
            if (current is { IsCompleted: false })
            {
                return;
            }

            logger.LogInformation("Cached {name} is {ageMinutes:F0} min old, older than the soft TTL - refreshing in the background", name, age.TotalMinutes);
            current = Task.Run(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await refresh();
                    logger.LogInformation("Background refresh of {name} completed in {elapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background refresh of {name} failed after {elapsedMs} ms, keeping the previous data: {message}", name, stopwatch.ElapsedMilliseconds, ex.Message);
                }
            });
        }
    }
}
