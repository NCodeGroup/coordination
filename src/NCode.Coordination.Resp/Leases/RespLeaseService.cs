// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Leases;

/// <summary>
/// The leases of the RESP provider: each operation is one lease script that runs over the resource's lease key.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespLeaseService(RespServer server) : ILeaseService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    public async ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);
        RespCall.Check(options, cancellationToken);

        var keyspace = resource.Keyspace;
        var resourceId = RespKeys.Name(resource);

        var sessionId = terms.IsBoundToSession ? terms.Session.Value : "";
        var ttlMs = terms.TimeToLive is not null ? RespCall.ToMilliseconds(terms.TimeToLive) : 0;

        var keys = new[]
        {
            RespKeys.ForKeyspace(keyspace, "leases"),
            RespKeys.ForKeyspace(keyspace, "lease-fencing"),
        };

        var arguments = new RedisValue[]
        {
            "tryAcquire",
            resourceId,
            "", // ownerToken (unused on acquire)
            sessionId,
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Lease, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length >= 3 => LeaseAcquireResult.Acquired(
                new LeaseGrant(
                    resource,
                    new FencingToken(
                        int.Parse(
                            (string?)fields[1] ?? throw RespReplies.Unexpected(reply),
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    ),
                    new OwnerToken((string?)fields[0] ?? throw RespReplies.Unexpected(reply)),
                    terms
                )
            ),
            1 => LeaseAcquireResult.Held,
            2 => LeaseAcquireResult.SessionEnded,
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> RenewAsync(
        LeaseGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(timeToLive);
        RespCall.Check(options, cancellationToken);

        var keyspace = grant.Resource.Keyspace;
        var resourceId = RespKeys.Name(grant.Resource);
        var ttlMs = RespCall.ToMilliseconds(timeToLive);

        var keys = new[]
        {
            RespKeys.ForKeyspace(keyspace, "leases"),
            RespKeys.ForKeyspace(keyspace, "lease-fencing"),
        };

        var arguments = new RedisValue[]
        {
            "renew",
            resourceId,
            grant.Owner.Value,
            "", // sessionId (unused on renew)
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Lease, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status == 0;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        RespCall.Check(options, cancellationToken);

        var keyspace = grant.Resource.Keyspace;
        var resourceId = RespKeys.Name(grant.Resource);

        var keys = new[]
        {
            RespKeys.ForKeyspace(keyspace, "leases"),
            RespKeys.ForKeyspace(keyspace, "lease-fencing"),
        };

        var arguments = new RedisValue[]
        {
            "release",
            resourceId,
            grant.Owner.Value,
            "", // sessionId (unused on release)
            "0", // ttl (unused on release)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Lease, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status == 0;
    }

    /// <inheritdoc />
    public async ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        RespCall.Check(options, cancellationToken);

        var keyspace = resource.Keyspace;
        var resourceId = RespKeys.Name(resource);

        // Poll for lease release with exponential backoff (100ms, up to 300 times = 30 seconds max)
        const int maxRetries = 300;
        const int delayMs = 100;

        var keys = new[]
        {
            RespKeys.ForKeyspace(keyspace, "leases"),
            RespKeys.ForKeyspace(keyspace, "lease-fencing"),
        };

        for (int i = 0; i < maxRetries; i++)
        {
            var arguments = new RedisValue[]
            {
                "check-held",
                resourceId,
                "", // ownerToken (unused on check)
                "", // sessionId (unused on check)
                "0", // ttl (unused on check)
            };

            var reply = await Server
                .EvaluateAsync(RespScript.Lease, keys, arguments, cancellationToken)
                .ConfigureAwait(false);

            var (status, _) = RespReplies.Script(reply);

            if (status == 1)
                return; // Not held; lease released

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
