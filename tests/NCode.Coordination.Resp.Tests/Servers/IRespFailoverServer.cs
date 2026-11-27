// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// A primary and its replica in Docker of one test's own, which the test fails over: it promotes the replica while the
/// primary still takes writes, then demotes the primary.
/// </summary>
internal interface IRespFailoverServer : IAsyncDisposable
{
    /// <summary>
    /// Gets the <c>host:port</c> list of the primary and the replica.
    /// </summary>
    string Endpoint { get; }

    /// <summary>
    /// Waits until the replica has every write of the primary, so the writes made so far survive the failover.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the replica has caught up.</returns>
    ValueTask WaitForReplicaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Promotes the replica without the primary's consent, so the primary goes on taking writes until it is demoted.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the promotion.</param>
    /// <returns>A task that completes when the replica serves as a primary.</returns>
    ValueTask PromoteReplicaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Demotes the old primary, which drops the writes it took after the promotion.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the demotion.</param>
    /// <returns>A task that completes when the old primary no longer takes writes.</returns>
    ValueTask DemotePrimaryAsync(CancellationToken cancellationToken);
}
