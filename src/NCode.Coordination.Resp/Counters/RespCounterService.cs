// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Counters;

/// <summary>
/// The counters of the RESP provider: each is one server integer that expires with the time to live of its first
/// increment, and every increment runs as one Lua script.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal sealed class RespCounterService(RespServer server) : ICounterService
{
    /// <summary>
    /// The kind of server key that holds a counter.
    /// </summary>
    private const string Kind = "counter";

    // ADR-1026 records why the largest count is 2^53 - 1.
    /// <summary>
    /// The largest count a counter keeps: 2^53 − 1, the largest integer a Lua number holds exactly.
    /// </summary>
    private const long LargestCount = 9_007_199_254_740_991;

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    /// <remarks>
    /// The operation is the counter script with one increment whose limit is <see cref="long.MaxValue" />, so it
    /// never exceeds.
    /// </remarks>
    public async ValueTask<long> IncrementAsync(
        CoordinationKey key,
        long amount,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        RespCall.Check(options, cancellationToken);

        var increment = new CounterIncrement(key, amount, long.MaxValue, timeToLive);
        var result = await RunAsync([increment], cancellationToken).ConfigureAwait(false);

        return result.Counts[^1];
    }

    /// <inheritdoc />
    public async ValueTask<long> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var reply = await Server
            .ExecuteAsync("GET", [serverKey], cancellationToken)
            .ConfigureAwait(false);

        return reply.IsNull ? 0 : RespReplies.Integer(reply);
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        await Server.ExecuteAsync("DEL", [serverKey], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(increments);

        if (increments.Count < 1 || increments.Count > CoordinationLimits.MaxBatchIncrements)
        {
            throw new ArgumentException(
                $"A batch has 1 to {CoordinationLimits.MaxBatchIncrements} increments.",
                nameof(increments)
            );
        }

        Keyspace? keyspace = null;
        foreach (var increment in increments)
        {
            if (increment is null)
            {
                throw new ArgumentException("A batch holds no null increment.", nameof(increments));
            }

            keyspace ??= increment.Key.Keyspace;
            if (increment.Key.Keyspace != keyspace)
            {
                throw new ArgumentException(
                    "Every increment of a batch is in one keyspace.",
                    nameof(increments)
                );
            }
        }

        RespCall.Check(options, cancellationToken);

        return await RunAsync(increments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the counter script over the increments of a checked batch.
    /// </summary>
    /// <param name="increments">The increments, in the order to apply them.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns>The counts the script applied, and the increment that exceeded its limit, if any.</returns>
    /// <exception cref="CoordinationException">
    /// An increment would pass the largest count, or the server answered with a reply the provider doesn't expect.
    /// </exception>
    private async ValueTask<CounterBatchResult> RunAsync(
        IReadOnlyList<CounterIncrement> increments,
        CancellationToken cancellationToken
    )
    {
        var keys = new RedisKey[increments.Count];
        var arguments = new RedisValue[increments.Count * 3];
        for (var index = 0; index < increments.Count; index++)
        {
            var increment = increments[index];
            keys[index] = RespKeys.ForKey(increment.Key, Kind);
            arguments[3 * index] = increment.Amount;
            arguments[3 * index + 1] = increment.Limit;
            arguments[3 * index + 2] = RespCall.ToMilliseconds(increment.TimeToLive);
        }

        var reply = await Server
            .EvaluateAsync(RespScript.CounterIncrement, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 1:
                throw new CoordinationException(
                    CoordinationErrorCodes.LimitExceeded,
                    outcomeUnknown: false,
                    $"A RESP counter keeps counts up to {LargestCount}, and an increment would pass it."
                );

            case 0 when fields.Length >= 2:
                var exceeded = RespReplies.Integer(fields[0]);
                var counts = new long[fields.Length - 1];
                for (var index = 0; index < counts.Length; index++)
                {
                    counts[index] = RespReplies.Integer(fields[index + 1]);
                }

                if (exceeded != 0 && exceeded != counts.Length)
                {
                    throw RespReplies.Unexpected(reply);
                }

                return new CounterBatchResult(counts)
                {
                    ExceededIndex = exceeded == 0 ? null : (int)exceeded - 1,
                };

            default:
                throw RespReplies.Unexpected(reply);
        }
    }
}
