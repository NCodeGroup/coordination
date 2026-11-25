// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The terms of a barrier: how many parties must arrive, and how long a party's arrival lasts.
/// </summary>
public sealed class BarrierTerms
{
    /// <summary>
    /// Creates the terms of a barrier.
    /// </summary>
    /// <param name="parties">How many parties must arrive for the barrier to open.</param>
    /// <param name="lifetime">How long the record of a party's arrival lasts.</param>
    /// <exception cref="ArgumentNullException">The lifetime is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The parties are below 1 or above the page size limit.
    /// </exception>
    public BarrierTerms(int parties, RecordLifetime lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parties, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parties, CoordinationLimits.MaxPageSize);
        ArgumentNullException.ThrowIfNull(lifetime);

        Parties = parties;
        Lifetime = lifetime;
    }

    /// <summary>
    /// Gets how many parties must arrive for the barrier to open.
    /// </summary>
    public int Parties { get; }

    /// <summary>
    /// Gets how long the record of a party's arrival lasts.
    /// </summary>
    public RecordLifetime Lifetime { get; }
}
