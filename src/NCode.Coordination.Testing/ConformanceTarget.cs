// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing;

/// <summary>
/// The provider a contract test runs against, created for that one test, and optionally a way to advance the
/// provider's clock.
/// </summary>
/// <param name="provider">The provider under test.</param>
/// <exception cref="ArgumentNullException">The provider is null.</exception>
public sealed class ConformanceTarget(ICoordinationProvider provider)
{
    /// <summary>
    /// Gets the provider under test.
    /// </summary>
    public ICoordinationProvider Provider { get; } =
        provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>
    /// Gets a delegate that moves the provider's clock forward by the duration, by moving a fake clock or by waiting,
    /// or null when the provider's clock cannot be advanced.
    /// </summary>
    public Func<TimeSpan, CancellationToken, ValueTask>? AdvanceClock { get; init; }
}
