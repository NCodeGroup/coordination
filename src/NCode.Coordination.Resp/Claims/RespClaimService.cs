// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Claims;

/// <summary>
/// The claims of the RESP provider: each is one server key, named by the claim's name and window, that the server
/// expires on its own clock.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespClaimService(RespServer server) : IClaimService
{
    /// <summary>
    /// The kind of server key that holds a claim.
    /// </summary>
    private const string Kind = "claim";

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
    /// The operation is one <c>SET … NX PX</c> command on the key of the name followed by the window's segment, with
    /// the caller's time to live, which already carries its skew allowance. It fails closed: it answers claimed only
    /// for the server's <c>OK</c>, a null reply means the window is taken, and any other reply throws.
    /// </remarks>
    /// <exception cref="ArgumentException">The name has no room for the window's segment.</exception>
    public async ValueTask<ClaimStatus> TryClaimAsync(
        CoordinationKey name,
        TimeWindow window,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(timeToLive);

        if (name.Segments.Length >= CoordinationKey.MaxSegments)
        {
            throw new ArgumentException(
                $"A claim's name has at most {CoordinationKey.MaxSegments - 1} segments, so the window's segment fits after them.",
                nameof(name)
            );
        }

        RespCall.Check(options, cancellationToken);

        var key = new CoordinationKey(name.Keyspace, [.. name.Segments, window.Segment])
        {
            PartitionDepth = name.PartitionDepth,
        };

        var serverKey = RespKeys.ForKey(key, Kind);
        var ms = RespCall.ToMilliseconds(timeToLive).ToString(CultureInfo.InvariantCulture);
        var reply = await Server
            .ExecuteAsync(RespCommands.Set, [serverKey, "1", "NX", "PX", ms], cancellationToken)
            .ConfigureAwait(false);

        if (reply.IsNull)
        {
            return ClaimStatus.Taken;
        }

        if (reply.Resp2Type == ResultType.SimpleString && (string?)reply == Accepted)
        {
            return ClaimStatus.Claimed;
        }

        throw RespReplies.Unexpected(reply);
    }
}
