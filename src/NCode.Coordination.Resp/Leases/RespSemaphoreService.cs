// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Leases;

/// <summary>
/// The semaphores of the RESP provider: each operation is one semaphore script that manages slots on the resource.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespSemaphoreService(RespServer server) : ISemaphoreService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    public async ValueTask<SemaphoreAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        int capacity,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, CoordinationLimits.MaxSemaphoreCapacity);
        ArgumentNullException.ThrowIfNull(terms);
        RespCall.Check(options, cancellationToken);

        var keyspace = resource.Keyspace;
        var resourceId = RespKeys.Name(resource);

        var sessionId = terms.IsBoundToSession ? terms.Session.Value : "";
        var ttlMs = terms.TimeToLive is not null
            ? RespCall.ToMilliseconds(terms.TimeToLive)
            : 0;

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "semaphores"),
            RespKeys.ForKeyspace(keyspace, "semaphore-fencing")
        };

        var arguments = new RedisValue[]
        {
            "tryAcquire",
            resourceId,
            capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "",  // ownerToken (unused on acquire)
            sessionId,
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Semaphore, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length >= 4 =>
                SemaphoreAcquireResult.Acquired(new SemaphoreGrant(
                    resource,
                    int.Parse((string?)fields[0] ?? throw RespReplies.Unexpected(reply), System.Globalization.CultureInfo.InvariantCulture) - 1,  // Convert 1-based to 0-based
                    new FencingToken(int.Parse((string?)fields[2] ?? throw RespReplies.Unexpected(reply), System.Globalization.CultureInfo.InvariantCulture)),
                    new OwnerToken((string?)fields[1] ?? throw RespReplies.Unexpected(reply)),
                    terms
                )),
            1 => SemaphoreAcquireResult.Full,
            2 => SemaphoreAcquireResult.SessionEnded,
            3 => SemaphoreAcquireResult.CapacityConflict,
            _ => throw RespReplies.Unexpected(reply)
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> RenewAsync(
        SemaphoreGrant grant,
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

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "semaphores"),
            RespKeys.ForKeyspace(keyspace, "semaphore-fencing")
        };

        var arguments = new RedisValue[]
        {
            "renew",
            resourceId,
            "1",  // unused capacity
            grant.Owner.Value,
            "",  // sessionId (unused on renew)
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (grant.Slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)  // Convert 0-based to 1-based slot
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Semaphore, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status == 0;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAsync(
        SemaphoreGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        RespCall.Check(options, cancellationToken);

        var keyspace = grant.Resource.Keyspace;
        var resourceId = RespKeys.Name(grant.Resource);

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "semaphores"),
            RespKeys.ForKeyspace(keyspace, "semaphore-fencing")
        };

        var arguments = new RedisValue[]
        {
            "release",
            resourceId,
            "1",  // unused capacity
            grant.Owner.Value,
            "",  // sessionId (unused on release)
            "0",  // ttl (unused on release)
            (grant.Slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)  // Convert 0-based to 1-based slot
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Semaphore, keys, arguments, cancellationToken)
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

        // Poll for a free slot with exponential backoff (100ms, up to 300 times = 30 seconds max)
        const int maxRetries = 300;
        const int delayMs = 100;

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "semaphores"),
            RespKeys.ForKeyspace(keyspace, "semaphore-fencing")
        };

        for (int i = 0; i < maxRetries; i++)
        {
            // We don't know the capacity, so pass 1 as placeholder; check-capacity just verifies semaphore exists
            var arguments = new RedisValue[]
            {
                "check-capacity",
                resourceId,
                "1",  // placeholder capacity
                "",  // ownerToken (unused on check)
                "",  // sessionId (unused on check)
                "0"  // ttl (unused on check)
            };

            var reply = await Server
                .EvaluateAsync(RespScript.Semaphore, keys, arguments, cancellationToken)
                .ConfigureAwait(false);

            var (status, _) = RespReplies.Script(reply);

            // Status 1 means semaphore doesn't exist (all slots released)
            if (status == 1)
                return;

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
