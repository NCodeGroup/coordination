// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
        var clients = new ConcurrentRunClients(steps, step);
        if (fault is null)
        {
            clients.Begun.SetResult();
        }

        var tasks = new List<Task>(Clients + 1);
        for (var client = 0; client < Clients; client++)
        {
            var current = client;
            var task = Task.Run(
                async () => await clients.RunClientAsync(current, cancellationToken),
                CancellationToken.None
            );
            tasks.Add(task);
        }

        if (fault is not null)
        {
            var task = Task.Run(
                async () =>
                {
                    await clients.Halfway.Task;

                    // The fault has begun once its delegate returns its task, and one that throws as it begins
                    // still releases the clients.
                    ValueTask pending;
                    try
                    {
                        pending = fault(cancellationToken);
                    }
                    finally
                    {
                        clients.Begun.TrySetResult();
                    }

                    await pending;
                },
                CancellationToken.None
            );
            tasks.Add(task);
        }

        clients.Gate.SetResult();

        await Task.WhenAll(tasks);
    }
}
