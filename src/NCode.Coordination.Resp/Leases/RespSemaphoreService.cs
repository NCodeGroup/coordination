// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Leases;

/// <summary>
/// The semaphores of the RESP provider: each slot is a grant in the keyspace's <c>grants</c> hash, a grant bound to a
/// session lives as its session does and a grant with a time to live has a deadline in <c>grant-deadlines</c>. One
/// keyspace script serves each operation, every grant has its own owner token, and a wait for a release polls until
/// a slot is free or the caller cancels.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The table or the tokens are null.</exception>
internal class RespSemaphoreService(RespRecordTable table, IRespTokens tokens) : ISemaphoreService
{
    /// <summary>
    /// The text an owner token starts with.
    /// </summary>
    private const string OwnerPrefix = "o-";

    /// <summary>
    /// Gets the table that runs the keyspace scripts.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the source of owner tokens.
    /// </summary>
    private IRespTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            capacity,
            CoordinationLimits.MaxSemaphoreCapacity
        );
        ArgumentNullException.ThrowIfNull(terms);
        RespCall.Check(options, cancellationToken);

        RedisValue[] arguments =
        [
            RespKeys.Name(resource),
            capacity,
            Tokens.Next(OwnerPrefix),
            terms.IsBoundToSession ? terms.Session.Value : "",
            terms.IsBoundToSession ? 0 : RespCall.ToMilliseconds(terms.TimeToLive),
        ];
        var reply = await Table
            .EvaluateAsync(
                RespScript.SemaphoreAcquire,
                resource.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 3 => ToAcquired(resource, terms, reply, fields),
            1 when fields.Length == 0 => SemaphoreAcquireResult.Full,
            2 when fields.Length == 0 => SemaphoreAcquireResult.SessionEnded,
            3 when fields.Length == 0 => SemaphoreAcquireResult.CapacityConflict,
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <summary>
    /// Builds the result of a semaphore slot that the server granted.
    /// </summary>
    /// <param name="resource">The resource that the semaphore guards.</param>
    /// <param name="terms">The terms of the slot's grant.</param>
    /// <param name="reply">The reply of the script.</param>
    /// <param name="fields">The fields of the reply after its status: slot, owner token and fencing token.</param>
    /// <returns>The result that carries the slot's grant.</returns>
    /// <exception cref="CoordinationException">The owner token is missing from the reply.</exception>
    private static SemaphoreAcquireResult ToAcquired(
        CoordinationKey resource,
        LeaseTerms terms,
        RedisResult reply,
        RedisResult[] fields
    )
    {
        var slot = (int)RespReplies.Integer(fields[0]);
        var fencingValue = RespReplies.Integer(fields[2]);
        var fencing = new FencingToken(fencingValue);
        var ownerValue = (string?)fields[1] ?? throw RespReplies.Unexpected(reply);
        var owner = new OwnerToken(ownerValue);
        var grant = new SemaphoreGrant(resource, slot, fencing, owner, terms);
        var acquired = SemaphoreAcquireResult.Acquired(grant);
        return acquired;
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

        if (grant.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A grant bound to a session lives as its session does, so it is never renewed.",
                nameof(grant)
            );
        }

        RedisValue[] arguments =
        [
            SlotField(grant),
            grant.Owner.Value,
            RespCall.ToMilliseconds(timeToLive),
        ];
        var reply = await Table
            .EvaluateAsync(
                RespScript.GrantRenew,
                grant.Resource.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => true,
            1 => false,
            _ => throw RespReplies.Unexpected(reply),
        };
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

        RedisValue[] arguments = [SlotField(grant), grant.Owner.Value];
        var reply = await Table
            .EvaluateAsync(
                RespScript.GrantRelease,
                grant.Resource.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => true,
            1 => false,
            _ => throw RespReplies.Unexpected(reply),
        };
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

        await RespPolling
            .UntilAsync(token => HasFreeSlotAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the field of the <c>grants</c> hash that holds a semaphore slot.
    /// </summary>
    /// <param name="grant">The grant of the slot.</param>
    /// <returns>The slot's grant field.</returns>
    private static string SlotField(SemaphoreGrant grant)
    {
        var name = RespKeys.Name(grant.Resource);
        return string.Create(CultureInfo.InvariantCulture, $"s {grant.Slot} {name}");
    }

    /// <summary>
    /// Asks the server whether a semaphore has a free slot.
    /// </summary>
    /// <param name="resource">The resource that the semaphore guards.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when at least one slot of the semaphore is free.</returns>
    /// <exception cref="CoordinationException">The call failed or the reply was not one the script sends.</exception>
    private async ValueTask<bool> HasFreeSlotAsync(
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        RedisValue[] arguments = [RespKeys.Name(resource)];
        var reply = await Table
            .EvaluateAsync(
                RespScript.SemaphoreCheck,
                resource.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => false,
            1 => true,
            _ => throw RespReplies.Unexpected(reply),
        };
    }
}
