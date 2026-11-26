// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;

namespace NCode.Coordination.Resp.Sequences;

/// <summary>
/// The sequences of the RESP provider: each is one server counter that is never reset or expired.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespSequenceService(RespServer server) : ISequenceService
{
    /// <summary>
    /// The kind of server key that holds a sequence.
    /// </summary>
    private const string Kind = "sequence";

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one <c>INCR</c> command.
    /// </remarks>
    public async ValueTask<long> NextAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var reply = await Server
            .ExecuteAsync(RespCommands.Increment, [serverKey], cancellationToken)
            .ConfigureAwait(false);

        return RespReplies.Integer(reply);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one <c>INCRBY</c> command, whose reply is the last number of the block.
    /// </remarks>
    public async ValueTask<SequenceBlock> ReserveAsync(
        CoordinationKey key,
        int count,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, CoordinationLimits.MaxSequenceBlock);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var amount = count.ToString(CultureInfo.InvariantCulture);
        var reply = await Server
            .ExecuteAsync(RespCommands.IncrementBy, [serverKey, amount], cancellationToken)
            .ConfigureAwait(false);

        var last = RespReplies.Integer(reply);
        return new SequenceBlock(last - count + 1, count);
    }
}
