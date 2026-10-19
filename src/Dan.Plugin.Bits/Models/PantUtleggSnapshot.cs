using System;
using System.Collections.Generic;

namespace Dan.Plugin.Bits.Models;

/// <summary>
/// Cached PantUtlegg endpoints with the time they were read, for the soft time-to-live.
/// </summary>
public sealed class PantUtleggSnapshot
{
    public required IReadOnlyList<EndpointExternal> Endpoints { get; init; }

    public required DateTimeOffset RefreshedAt { get; init; }

    /// <summary>
    /// Root folder of the GitHub archive the snapshot was built from ("{owner}-{repo}-{sha}"), i.e. the data commit.
    /// </summary>
    public required string Source { get; init; }
}
