// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the null check of
// its time provider.
/// <summary>
/// The schedule of each subscription's periodic check: how often it runs, and the time provider that runs it and
/// the grace.
/// </summary>
/// <param name="Interval">The interval of the periodic check.</param>
/// <param name="Time">The time provider of the periodic check and the grace.</param>
/// <exception cref="ArgumentNullException">The time provider is null.</exception>
internal sealed record RespCheckSchedule(TimeSpan Interval, TimeProvider Time)
{
    /// <summary>
    /// Gets the time provider of the periodic check and the grace.
    /// </summary>
    public TimeProvider Time { get; } = Time ?? throw new ArgumentNullException(nameof(Time));
}
