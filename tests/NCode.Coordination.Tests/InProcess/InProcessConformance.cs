// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.Testing;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Creates the in-process provider as the conformance target of the contract suites, and behind a decorator whose
/// connections a test drops, for the dropped-connection runs.
/// </summary>
internal static class InProcessConformance
{
    /// <summary>
    /// Creates an in-process provider on a fake clock, with a way to advance that clock.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the creation.</param>
    /// <returns>The target of one contract test.</returns>
    public static ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var time = InProcessTesting.CreateTime();
        var provider = new InProcessProvider(time);
        var target = new ConformanceTarget(provider) { AdvanceClock = CreateAdvance(time) };

        return ValueTask.FromResult(target);
    }

    /// <summary>
    /// Creates an in-process provider on a fake clock behind a <c>DroppingProvider</c>, with a way to advance the
    /// clock and a way to drop the provider's connections.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the creation.</param>
    /// <returns>The target of one contract test.</returns>
    public static ValueTask<ConformanceTarget> CreateDroppingTargetAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var time = InProcessTesting.CreateTime();
        var inner = new InProcessProvider(time);
        var provider = new DroppingProvider(inner);
        var target = new ConformanceTarget(provider)
        {
            AdvanceClock = CreateAdvance(time),
            DropConnections = provider.Connection.DropAsync,
        };

        return ValueTask.FromResult(target);
    }

    /// <summary>
    /// Creates the delegate that advances a fake clock.
    /// </summary>
    /// <param name="time">The fake clock to advance.</param>
    /// <returns>A delegate that advances <paramref name="time" /> by a duration.</returns>
    private static Func<TimeSpan, CancellationToken, ValueTask> CreateAdvance(FakeTimeProvider time)
    {
        return (duration, _) =>
        {
            time.Advance(duration);
            return ValueTask.CompletedTask;
        };
    }
}
