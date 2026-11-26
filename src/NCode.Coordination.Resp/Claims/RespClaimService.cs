// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Claims;

/// <summary>
/// The claims of the RESP provider: each operation is one claim script that manages claims on a name and window.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespClaimService(RespServer server) : IClaimService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
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
        RespCall.Check(options, cancellationToken);

        var keyspace = name.Keyspace;
        var claimName = RespKeys.Name(name);
        var windowSegment = window.Segment.ToString();
        var ttlMs = RespCall.ToMilliseconds(timeToLive);
        var skewMs = 1000;  // Default 1-second skew allowance per ADR-1008

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "claims"),
            RespKeys.ForKeyspace(keyspace, "claims-deadlines")
        };

        var arguments = new RedisValue[]
        {
            "tryClaim",
            claimName,
            windowSegment,
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            skewMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Claim, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => ClaimStatus.Claimed,
            1 => ClaimStatus.Taken,
            _ => throw RespReplies.Unexpected(reply)
        };
    }
}
