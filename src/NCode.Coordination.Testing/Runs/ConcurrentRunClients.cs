// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// The clients of one concurrent run and the state they share: the gate they start at, the number of steps left before
/// half of all the steps are done, and the start of the fault.
/// </summary>
/// <param name="steps">The number of steps each client runs.</param>
/// <param name="step">The step to run, given the client, the index of the step and the run's token.</param>
internal sealed class ConcurrentRunClients(
    int steps,
    Func<int, int, CancellationToken, ValueTask> step
)
{
    /// <summary>
    /// The number of steps left before half of all the steps are done, which each client decrements atomically.
    /// </summary>
    private int _remaining = ConcurrentRun.Clients * steps / 2;

    /// <summary>
    /// Gets the source that completes when every client has started.
    /// </summary>
    public TaskCompletionSource Gate { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the source that completes when half of all the steps are done, or when a step throws.
    /// </summary>
    public TaskCompletionSource Halfway { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the source that completes when the fault has begun, or at once when the run has none.
    /// </summary>
    public TaskCompletionSource Begun { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the number of steps each client runs.
    /// </summary>
    private int Steps { get; } = steps;

    /// <summary>
    /// Gets the step to run, given the client, the index of the step and the run's token.
    /// </summary>
    private Func<int, int, CancellationToken, ValueTask> Step { get; } = step;

    /// <summary>
    /// Runs the steps of one client once the gate opens, and releases the fault when half of all the steps are done or
    /// when a step throws.
    /// </summary>
    /// <param name="client">The client that runs the steps.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when the client has run every step.</returns>
    public async ValueTask RunClientAsync(int client, CancellationToken cancellationToken)
    {
        try
        {
            await Gate.Task;

            for (var index = 0; index < Steps; index++)
            {
                if (Halfway.Task.IsCompleted)
                {
                    await Begun.Task.WaitAsync(cancellationToken);
                }

                await Step(client, index, cancellationToken);

                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    Halfway.TrySetResult();
                }

                await Task.Yield();
            }
        }
        catch
        {
            Halfway.TrySetResult();
            throw;
        }
    }
}
