// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// Drives several clients at once through the same steps, so that their calls overlap.
/// </summary>
internal static class ConcurrentRun
{
    /// <summary>
    /// The number of clients that run at once.
    /// </summary>
    public const int Clients = 3;

    /// <summary>
    /// The number of steps each client runs.
    /// </summary>
    public const int Steps = 12;

    /// <summary>
    /// Runs <see cref="Steps"/> steps on each of <see cref="Clients"/> clients, with no fault.
    /// </summary>
    /// <param name="step">The step to run, given the client, the index of the step and the run's token.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when every client has run every step.</returns>
    public static ValueTask RunAsync(
        Func<int, int, CancellationToken, ValueTask> step,
        CancellationToken cancellationToken
    ) => RunAsync(Steps, step, null, cancellationToken);

    /// <summary>
    /// Runs the steps on each client at once, and a fault once half of all the steps are done.
    /// </summary>
    /// <remarks>
    /// Every client waits at a gate that opens when all of them have started, and yields after each step. A client whose
    /// step throws releases the fault, so a fault never waits for steps that will not run, and rethrows. A client that
    /// finds half of the steps done waits until the fault has begun, so the calls of the second half meet the fault
    /// however late it is scheduled.
    /// </remarks>
    /// <param name="steps">The number of steps each client runs.</param>
    /// <param name="step">The step to run, given the client, the index of the step and the run's token.</param>
    /// <param name="fault">The fault to inject, or null for none.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when every client has run every step and the fault has run.</returns>
    public static async ValueTask RunAsync(
        int steps,
        Func<int, int, CancellationToken, ValueTask> step,
        Func<CancellationToken, ValueTask>? fault,
        CancellationToken cancellationToken
    )
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (fault is null)
        {
            begun.SetResult();
        }

        var remaining = new StrongBox<int>(Clients * steps / 2);

        var tasks = new List<Task>(Clients + 1);
        for (var client = 0; client < Clients; client++)
        {
            var current = client;
            var task = Task.Run(
                async () =>
                    await RunClientAsync(
                        current,
                        steps,
                        step,
                        gate.Task,
                        halfway,
                        begun.Task,
                        remaining,
                        cancellationToken
                    ),
                CancellationToken.None
            );
            tasks.Add(task);
        }

        if (fault is not null)
        {
            var task = Task.Run(
                async () =>
                {
                    await halfway.Task;

                    // The fault has begun once its delegate returns its task, and one that throws as it begins
                    // still releases the clients.
                    ValueTask pending;
                    try
                    {
                        pending = fault(cancellationToken);
                    }
                    finally
                    {
                        begun.TrySetResult();
                    }

                    await pending;
                },
                CancellationToken.None
            );
            tasks.Add(task);
        }

        gate.SetResult();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Runs the steps of one client once the gate opens, and releases the fault when half of all the steps are done or
    /// when a step throws.
    /// </summary>
    /// <param name="client">The client that runs the steps.</param>
    /// <param name="steps">The number of steps the client runs.</param>
    /// <param name="step">The step to run, given the client, the index of the step and the run's token.</param>
    /// <param name="gate">The task that completes when every client has started.</param>
    /// <param name="halfway">The source that completes when half of all the steps are done.</param>
    /// <param name="begun">The task that completes when the fault has begun, or at once when there is none.</param>
    /// <param name="remaining">The number of steps left before half of all the steps are done.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when the client has run every step.</returns>
    private static async ValueTask RunClientAsync(
        int client,
        int steps,
        Func<int, int, CancellationToken, ValueTask> step,
        Task gate,
        TaskCompletionSource halfway,
        Task begun,
        StrongBox<int> remaining,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await gate;

            for (var index = 0; index < steps; index++)
            {
                if (halfway.Task.IsCompleted)
                {
                    await begun.WaitAsync(cancellationToken);
                }

                await step(client, index, cancellationToken);

                if (Interlocked.Decrement(ref remaining.Value) == 0)
                {
                    halfway.TrySetResult();
                }

                await Task.Yield();
            }
        }
        catch
        {
            halfway.TrySetResult();
            throw;
        }
    }
}
