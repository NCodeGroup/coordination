// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Leases;

/// <summary>
/// The leases of the RESP provider: each lease is a grant in the keyspace's <c>grants</c> hash, a grant bound to a
/// session lives as its session does and a grant with a time to live has a deadline in <c>grant-deadlines</c>. One
/// keyspace script serves each operation, every grant has its own owner token, and a wait for a release polls until
/// the lease is free or the caller cancels.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The table or the tokens are null.</exception>
internal class RespLeaseService(RespRecordTable table, IRespTokens tokens) : ILeaseService
{
    /// <summary>
    /// The text an owner token starts with.
    /// </summary>
    private const string OwnerPrefix = "o-";

    /// <summary>
    /// The text a lease's grant field starts with.
    /// </summary>
    private const string FieldPrefix = "l ";

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

        RedisValue[] arguments =
        [
            RespKeys.Name(resource),
            Tokens.Next(OwnerPrefix),
            terms.IsBoundToSession ? terms.Session.Value : "",
            terms.IsBoundToSession ? 0 : RespCall.ToMilliseconds(terms.TimeToLive),
        ];
        var reply = await Table
            .EvaluateAsync(RespScript.LeaseAcquire, resource.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 2 => LeaseAcquireResult.Acquired(
                new LeaseGrant(
                    resource,
                    new FencingToken(RespReplies.Integer(fields[1])),
                    new OwnerToken((string?)fields[0] ?? throw RespReplies.Unexpected(reply)),
                    terms
                )
            ),
            1 when fields.Length == 0 => LeaseAcquireResult.Held,
            2 when fields.Length == 0 => LeaseAcquireResult.SessionEnded,
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

        if (grant.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A grant bound to a session lives as its session does, so it is never renewed.",
                nameof(grant)
            );
        }

        RedisValue[] arguments =
        [
            FieldPrefix + RespKeys.Name(grant.Resource),
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
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        RespCall.Check(options, cancellationToken);

        RedisValue[] arguments = [FieldPrefix + RespKeys.Name(grant.Resource), grant.Owner.Value];
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
            .UntilAsync(token => IsFreeAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the server whether a lease is free.
    /// </summary>
    /// <param name="resource">The resource that the lease guards.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when no holder has the lease.</returns>
    /// <exception cref="CoordinationException">The call failed or the reply was not one the script sends.</exception>
    private async ValueTask<bool> IsFreeAsync(
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        RedisValue[] arguments = [FieldPrefix + RespKeys.Name(resource)];
        var reply = await Table
            .EvaluateAsync(RespScript.GrantCheck, resource.Keyspace, arguments, cancellationToken)
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
