using System;
using System.Linq;
using System.Threading.Tasks;
using Dan.Plugin.Bits.Config;
using Dan.Plugin.Bits.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dan.Plugin.Bits.Test;

/// <summary>
/// Talks to the real data-altinn-no/bits repo. Skipped unless BITS_GITHUB_PAT is set, e.g.
/// <c>$env:BITS_GITHUB_PAT = (gh auth token); dotnet test</c>.
/// </summary>
public class GithubClientIntegrationTests
{
    private static readonly string? Pat = Environment.GetEnvironmentVariable("BITS_GITHUB_PAT");

    [SkippableFact]
    public async Task Downloads_repository_archive_and_finds_test_files()
    {
        Skip.If(string.IsNullOrEmpty(Pat), "BITS_GITHUB_PAT not set");

        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();

        var client = new GithubClient(
            provider.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
            Options.Create(new Settings { GithubPat = Pat }),
            NullLoggerFactory.Instance);

        var archive = RepositoryArchive.FromZip(await client.DownloadRepositoryArchive());

        Assert.StartsWith("data-altinn-no-bits-", archive.RootFolder);
        Assert.True(archive.TryGetFile("test/Banker_Kontroll_test.csv", out var csv));
        Assert.Contains("orgnummer", csv);

        var limitationFiles = archive.FilesInDirectory("test/limitations/")
            .Where(f => f.Name.EndsWith(".json") && !f.Name.StartsWith('.'))
            .ToList();
        Assert.NotEmpty(limitationFiles);
        Assert.All(limitationFiles, f => Assert.Matches(@"^\d{9}\.json$", f.Name));
    }
}
