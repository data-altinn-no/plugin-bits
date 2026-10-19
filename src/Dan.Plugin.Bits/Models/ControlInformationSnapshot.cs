using System;
using System.Collections.Generic;

namespace Dan.Plugin.Bits.Models;

/// <summary>
/// Everything Kontrollinformasjon, KontrollinformasjonUtvidet and Begrensninger answer from, built in one go and
/// swapped atomically so a request never observes endpoints without their limitations.
/// </summary>
public sealed class ControlInformationSnapshot
{
    /// <summary>
    /// All endpoints from the CSV (active and inactive) with limitations attached. Instances are never mutated after the snapshot is built.
    /// </summary>
    public required IReadOnlyList<EndpointExternal> Endpoints { get; init; }

    /// <summary>
    /// Limitations per organisation number, for every limitation file present in the repo.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<Limitation>> LimitationsByOrgNo { get; init; }

    public required DateTimeOffset RefreshedAt { get; init; }

    /// <summary>
    /// Root folder of the GitHub archive the snapshot was built from ("{owner}-{repo}-{sha}"), i.e. the data commit.
    /// </summary>
    public required string Source { get; init; }
}
