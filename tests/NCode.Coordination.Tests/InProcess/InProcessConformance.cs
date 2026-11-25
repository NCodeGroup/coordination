// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Creates the in-process provider as the conformance target of the contract suites.
/// </summary>
internal static class InProcessConformance
{
    /// <summary>
    /// Creates an in-process provider on a fake clock, with a way to advance that clock.
    /// </summary>
    /// <returns>The target of one contract test.</returns>
    public static ValueTask<ConformanceTarget> CreateTargetAsync()
    {
        var time = InProcessTesting.CreateTime();
        var provider = new InProcessProvider(time);
        var target = new ConformanceTarget(provider)
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
