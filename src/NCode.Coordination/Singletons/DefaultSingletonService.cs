// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Singletons;

/// <summary>
/// Runs work on the leader of a role, with an election for the role and a watch that cancels the work when the
/// leadership ends.
/// </summary>
/// <param name="elections">The election operations the singleton is built on.</param>
internal class DefaultSingletonService(IElectionService elections) : ISingletonService
{
    /// <summary>
    /// Gets the election operations that choose and watch the leader of a role.
    /// </summary>
    private IElectionService Elections { get; } = elections;

    /// <inheritdoc />
    public async ValueTask<bool> RunAsync(
        CoordinationKey role,
        SingletonCandidate candidate,
        Func<Leadership, CancellationToken, ValueTask> work,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(work);

        var terms = LeaseTerms.ForSession(candidate.Session);

        while (true)
        {
            var result = await Elections
                .TryLeadAsync(role, candidate.Value, terms, options, cancellationToken)
                .ConfigureAwait(false);

            if (result.Status == ElectionStatus.SessionEnded)
            {
                return false;
            }

            if (!result.IsElected)
            {
                await WaitForNoLeaderAsync(role, options, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var leadership = result.Leadership;

            using var lost = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var monitor = MonitorAsync(role, leadership, lost, options);

            try
            {
                await work(leadership, lost.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (lost.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The leadership ended while the work ran: campaign again.
                await StopAsync(lost, monitor).ConfigureAwait(false);
                continue;
            }
            catch
            {
                await StopAsync(lost, monitor).ConfigureAwait(false);
                await ResignQuietlyAsync(leadership, options).ConfigureAwait(false);
                throw;
            }

            await StopAsync(lost, monitor).ConfigureAwait(false);
            await ResignQuietlyAsync(leadership, options).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Waits until the role has no leader.
    /// </summary>
    /// <param name="role">The role to watch.</param>
    /// <param name="options">Options for the watch, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the role has no leader.</returns>
    private async ValueTask WaitForNoLeaderAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        await foreach (
            var view in Elections.WatchAsync(role, options, cancellationToken).ConfigureAwait(false)
        )
        {
            if (view.Leader is null)
            {
                return;
            }
        }

        throw new InvalidOperationException("The watch ended without a failure.");
    }

    /// <summary>
    /// Watches a role and cancels the source when the role shows another leader or none.
    /// </summary>
    /// <param name="role">The role to watch.</param>
    /// <param name="leadership">The leadership to vouch for.</param>
    /// <param name="lost">The source to cancel when the leadership ends, whose token also stops the watch.</param>
    /// <param name="options">Options for the watch, or null for none.</param>
    /// <returns>A task that completes when the watch has stopped.</returns>
    private async Task MonitorAsync(
        CoordinationKey role,
        Leadership leadership,
        CancellationTokenSource lost,
        OperationOptions? options
    )
    {
        try
        {
            await foreach (
                var view in Elections.WatchAsync(role, options, lost.Token).ConfigureAwait(false)
            )
            {
                if (view.Leader is null || view.Leader.Term != leadership.Leader.Term)
                {
                    await lost.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (lost.IsCancellationRequested)
        {
            // The work finished, or the leadership ended, or the call was cancelled.
        }
        catch (CoordinationException)
        {
            // A failed watch cannot vouch for the leadership.
            await lost.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the monitor and waits for it.
    /// </summary>
    /// <param name="lost">The source whose cancellation stops the monitor.</param>
    /// <param name="monitor">The monitor to wait for.</param>
    /// <returns>A task that completes when the monitor has stopped.</returns>
    private static async ValueTask StopAsync(CancellationTokenSource lost, Task monitor)
    {
        await lost.CancelAsync().ConfigureAwait(false);
        await monitor.ConfigureAwait(false);
    }

    /// <summary>
    /// Gives up a leadership, treating a provider failure as the leadership ending with its session.
    /// </summary>
    /// <param name="leadership">The leadership to give up.</param>
    /// <param name="options">Options for the call, or null for none.</param>
    /// <returns>True when the leadership was given up; false when it was not, or the provider failed.</returns>
    private async ValueTask<bool> ResignQuietlyAsync(
        Leadership leadership,
        OperationOptions? options
    )
    {
        try
        {
            return await Elections
                .ResignAsync(leadership, options, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (CoordinationException)
        {
            // The leadership still ends with its session.
            return false;
        }
    }
}
