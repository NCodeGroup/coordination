// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The terms of a single-flight fetch: how long the fetched value lives, and how long a fetch may hold its turn.
/// </summary>
public sealed class SingleFlightTerms
{
    /// <summary>
    /// Creates the terms of a single-flight fetch.
    /// </summary>
    /// <param name="valueTimeToLive">How long the fetched value lives.</param>
    /// <param name="fetchTimeToLive">How long the fetching caller holds its turn before another may fetch.</param>
    /// <exception cref="ArgumentNullException">A time to live is null.</exception>
    public SingleFlightTerms(TimeToLive valueTimeToLive, TimeToLive fetchTimeToLive)
    {
        ArgumentNullException.ThrowIfNull(valueTimeToLive);
        ArgumentNullException.ThrowIfNull(fetchTimeToLive);

        ValueTimeToLive = valueTimeToLive;
        FetchTimeToLive = fetchTimeToLive;
    }

    /// <summary>
    /// Gets how long the fetched value lives.
    /// </summary>
    public TimeToLive ValueTimeToLive { get; }

    /// <summary>
    /// Gets how long the fetching caller holds its turn before another may fetch.
    /// </summary>
    public TimeToLive FetchTimeToLive { get; }
}
