using System;
using System.Threading;
using System.Threading.Tasks;
using Dan.Plugin.Bits.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dan.Plugin.Bits.Test;

public class CacheWarmupServiceTests
{
    private readonly IControlInformationService _service = Substitute.For<IControlInformationService>();

    [Fact]
    public async Task Refreshes_everything_on_start()
    {
        var warmup = new CacheWarmupService(_service, NullLoggerFactory.Instance);

        await warmup.StartAsync(CancellationToken.None);
        await warmup.ExecuteTask!;

        await _service.Received(1).RefreshAll();
    }

    [Fact]
    public async Task Failed_warmup_does_not_fail_start()
    {
        _service.RefreshAll().ThrowsAsync(new InvalidOperationException("github down"));
        var warmup = new CacheWarmupService(_service, NullLoggerFactory.Instance);

        await warmup.StartAsync(CancellationToken.None);
        await warmup.ExecuteTask!;

        await _service.Received(1).RefreshAll();
    }
}
