// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// Asks the nodes of the tests' own servers about their state, for the helpers that join, promote and demote them.
/// </summary>
internal static class RespNodes
{
    /// <summary>
    /// The start of the line of <c>INFO replication</c> that gives a node's replication offset.
    /// </summary>
    private const string OffsetField = "master_repl_offset:";

    /// <summary>
    /// The address that every node of the tests' own servers announces and that the tests connect to, so the client
    /// sees each node once and can reach it from the host.
    /// </summary>
    internal const string Host = "127.0.0.1";

    /// <summary>
    /// The line of <c>INFO replication</c> that says a replica's link to its primary is up.
    /// </summary>
    internal const string LinkUp = "master_link_status:up";

    /// <summary>
    /// The line of <c>CLUSTER INFO</c> that says a cluster serves its slots.
    /// </summary>
    internal const string ClusterStateOk = "cluster_state:ok";

    /// <summary>
    /// The command that reports a node's state; <c>CLUSTER</c> takes it as its subcommand too.
    /// </summary>
    internal const string InfoCommand = "INFO";

    /// <summary>
    /// The command that reads and changes a Garnet or Valkey node's cluster state.
    /// </summary>
    internal const string ClusterCommand = "CLUSTER";

    /// <summary>
    /// The command that makes a node follow another node, or, with the arguments <c>NO ONE</c>, stop following one.
    /// </summary>
    internal const string ReplicaOfCommand = "REPLICAOF";

    /// <summary>
    /// The section of the reply to <c>INFO</c> that reports a node's replication state.
    /// </summary>
    private const string ReplicationSection = "replication";

    /// <summary>
    /// Gets how long a wait asks a node again before it fails.
    /// </summary>
    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets how long a wait pauses between two questions to a node.
    /// </summary>
    private static TimeSpan WaitPause { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Connects to nodes with admin commands allowed.
    /// </summary>
    /// <param name="endpoint">The <c>host:port</c> list of the nodes.</param>
    /// <param name="cancellationToken">The token that cancels the connection.</param>
    /// <returns>The connection, which the caller disposes.</returns>
    public static async ValueTask<ConnectionMultiplexer> ConnectAdminAsync(
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        var options = ConfigurationOptions.Parse(endpoint);
        options.AllowAdmin = true;
        var connecting = ConnectionMultiplexer.ConnectAsync(options);
        return await connecting.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Asks a node a question until its reply holds, and fails when it doesn't within the limit.
    /// </summary>
    /// <param name="node">The node to ask.</param>
    /// <param name="command">The command to send, its name first.</param>
    /// <param name="holds">The check that the node's reply must pass.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the reply holds.</returns>
    /// <exception cref="TimeoutException">The reply never held within the limit.</exception>
    public static async ValueTask WaitForReplyAsync(
        IServer node,
        IReadOnlyList<string> command,
        Func<string, bool> holds,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        object[] arguments = [.. command.Skip(1)];
        while (true)
        {
            var executing = node.ExecuteAsync(command[0], arguments);
            var result = await executing.WaitAsync(cancellationToken);
            var reply = result.ToString();
            if (holds(reply))
            {
                return;
            }

            if (stopwatch.Elapsed > WaitLimit)
            {
                var asked = string.Join(" ", command);
                throw new TimeoutException($"{node.EndPoint} never answered '{asked}' as awaited.");
            }

            await Task.Delay(WaitPause, cancellationToken);
        }
    }

    /// <summary>
    /// Asks a node for its replication state until the reply holds, and fails when it doesn't within the limit.
    /// </summary>
    /// <param name="node">The node to ask.</param>
    /// <param name="holds">The check that the node's reply must pass.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the reply holds.</returns>
    /// <exception cref="TimeoutException">The reply never held within the limit.</exception>
    public static async ValueTask WaitForReplicationAsync(
        IServer node,
        Func<string, bool> holds,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string> command = [InfoCommand, ReplicationSection];
        await WaitForReplyAsync(node, command, holds, cancellationToken);
    }

    /// <summary>
    /// Waits until a replica has every write of its primary: both report the same replication offset.
    /// </summary>
    /// <param name="primary">The primary node.</param>
    /// <param name="replica">The replica of the primary.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the replica has caught up.</returns>
    /// <exception cref="TimeoutException">The replica never caught up within the limit.</exception>
    public static async ValueTask WaitForReplicaAsync(
        IServer primary,
        IServer replica,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var primaryOffset = await ReadOffsetAsync(primary, cancellationToken);
            var replicaOffset = await ReadOffsetAsync(replica, cancellationToken);
            if (string.Equals(primaryOffset, replicaOffset, StringComparison.Ordinal))
            {
                return;
            }

            if (stopwatch.Elapsed > WaitLimit)
            {
                throw new TimeoutException(
                    $"{replica.EndPoint} never caught up with {primary.EndPoint}."
                );
            }

            await Task.Delay(WaitPause, cancellationToken);
        }
    }

    /// <summary>
    /// Reads the line of a node's <c>INFO replication</c> reply that gives its replication offset.
    /// </summary>
    /// <param name="node">The node to ask.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The trimmed line.</returns>
    private static async ValueTask<string> ReadOffsetAsync(
        IServer node,
        CancellationToken cancellationToken
    )
    {
        var executing = node.ExecuteAsync(InfoCommand, ReplicationSection);
        var result = await executing.WaitAsync(cancellationToken);
        var reply = result.ToString();
        var lines = reply.Split('\n');
        var line = lines.First(text => text.StartsWith(OffsetField, StringComparison.Ordinal));
        return line.Trim();
    }
}
