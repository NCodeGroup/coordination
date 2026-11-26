// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Occurrences;

/// <summary>
/// The occurrences of the RESP provider: each is one server key that the server expires on its own clock.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespOccurrenceService(RespServer server) : IOccurrenceService
{
    /// <summary>
    /// The kind of server key that holds an occurrence.
    /// </summary>
    private const string Kind = "occurrence";

    /// <summary>
    /// The status text with which the server accepts a <c>SET … NX</c> that wrote the key.
    /// </summary>
    private const string Accepted = "OK";

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one <c>SET … NX PX</c> command. It fails closed: it answers recorded only for the server's
    /// <c>OK</c>, a null reply is a duplicate, and any other reply throws.
    /// </remarks>
    public async ValueTask<OccurrenceStatus> TryRecordAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var ms = RespCall.ToMilliseconds(timeToLive).ToString(CultureInfo.InvariantCulture);
        var reply = await Server
            .ExecuteAsync(RespCommands.Set, [serverKey, "1", "NX", "PX", ms], cancellationToken)
            .ConfigureAwait(false);

        if (reply.IsNull)
        {
            return OccurrenceStatus.Duplicate;
        }

        if (reply.Resp2Type == ResultType.SimpleString && (string?)reply == Accepted)
        {
            return OccurrenceStatus.Recorded;
        }

        throw RespReplies.Unexpected(reply);
    }
}
