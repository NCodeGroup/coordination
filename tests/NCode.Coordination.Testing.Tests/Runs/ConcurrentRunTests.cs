// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Runs.Tests;

public sealed class ConcurrentRunTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #region RunAsync Tests

    [Fact]
    public async ValueTask RunAsync_WhenStepsGiven_RunsEachStepOncePerClientInOrder()
    {
        var indexes = new List<int>[ConcurrentRun.Clients];
        for (var client = 0; client < indexes.Length; client++)
        {
            indexes[client] = [];
        }

        await ConcurrentRun.RunAsync(
            (client, index, _) =>
            {
                indexes[client].Add(index);
                return ValueTask.CompletedTask;
            },
            Token
        );

        var expected = Enumerable.Range(0, ConcurrentRun.Steps).ToArray();
        foreach (var clientIndexes in indexes)
        {
            Assert.Equal(expected, clientIndexes);
        }
    }

    [Fact]
    public async ValueTask RunAsync_WhenFaultGiven_RunsFaultOnceAfterHalfTheSteps()
    {
        const int steps = 6;
        var completed = 0;
        var faults = 0;
        var completedAtFault = 0;

        await ConcurrentRun.RunAsync(
            steps,
            (_, _, _) =>
            {
                Interlocked.Increment(ref completed);
                return ValueTask.CompletedTask;
            },
            _ =>
            {
                Interlocked.Increment(ref faults);
                completedAtFault = Volatile.Read(ref completed);
                return ValueTask.CompletedTask;
            },
            Token
        );

        Assert.Equal(1, faults);
        Assert.True(completedAtFault >= ConcurrentRun.Clients * steps / 2);
        Assert.Equal(ConcurrentRun.Clients * steps, completed);
    }

    [Fact]
    public async ValueTask RunAsync_WhenStepFails_Propagates()
    {
        var faults = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ConcurrentRun.RunAsync(
                ConcurrentRun.Steps,
                (client, index, _) =>
                    client == 1 && index == 2
                        ? ValueTask.FromException(new InvalidOperationException())
                        : ValueTask.CompletedTask,
                _ =>
                {
                    Interlocked.Increment(ref faults);
                    return ValueTask.CompletedTask;
                },
                Token
            )
        );

        Assert.Equal(1, faults);
    }

    #endregion
}
