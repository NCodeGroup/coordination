// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// A subscription of a <c>DroppingProvider</c>, which a drop of the connection marks.
/// </summary>
internal sealed class DroppingSubscription
{
    /// <summary>
    /// The flag that a drop sets and a read takes: 1 after a drop, 0 once it is taken.
    /// </summary>
    private int _dropped;

    /// <summary>
    /// Marks the subscription as dropped.
    /// </summary>
    public void Drop() => Volatile.Write(ref _dropped, 1);

    /// <summary>
    /// Takes the mark of a drop, so that one drop is taken once.
    /// </summary>
    /// <returns><see langword="true" /> when a drop had marked the subscription since the last take.</returns>
    public bool TakeDrop() => Interlocked.Exchange(ref _dropped, 0) == 1;
}
