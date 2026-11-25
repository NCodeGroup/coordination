// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.Testing;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Creates the in-process provider as the conformance target of the contract suites.
/// </summary>
internal static class InProcessConformance
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates an in-process provider on a fake clock, with a way to advance that clock.
    /// </summary>
    /// <returns>The target of one contract test.</returns>
    public static ValueTask<ConformanceTarget> CreateTargetAsync()
    {
        var time = new FakeTimeProvider(Start);
        var target = new ConformanceTarget(new InProcessProvider(time))
        {
            AdvanceClock = (duration, _) =>
            {
                time.Advance(duration);
                return ValueTask.CompletedTask;
            },
        };

        return ValueTask.FromResult(target);
    }
}
