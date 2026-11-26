// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Elections;

/// <summary>
/// The elections of the RESP provider: each operation is one election script that manages leader of a role.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespElectionService(RespServer server) : IElectionService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    public async ValueTask<ElectionResult> TryLeadAsync(
        CoordinationKey role,
        ReadOnlyMemory<byte> value,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(terms);
        RespCall.Check(options, cancellationToken);

        var keyspace = role.Keyspace;
        var roleId = RespKeys.Name(role);

        var sessionId = terms.IsBoundToSession ? terms.Session.Value : "";
        var ttlMs = terms.TimeToLive is not null
            ? RespCall.ToMilliseconds(terms.TimeToLive)
            : 0;

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "elections"),
            RespKeys.ForKeyspace(keyspace, "election-fencing")
        };

        var arguments = new RedisValue[]
        {
            "tryLead",
            roleId,
            value.Span.ToArray(),
            "",  // ownerToken (unused on acquire)
            sessionId,
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Election, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length >= 3 =>
                ElectionResult.Elected(new Leadership(
                    new Leader(
                        role,
                        new FencingToken(int.Parse((string?)fields[2] ?? throw RespReplies.Unexpected(reply), System.Globalization.CultureInfo.InvariantCulture)),
                        value
                    ),
                    new OwnerToken((string?)fields[1] ?? throw RespReplies.Unexpected(reply)),
                    terms
                )),
            1 => ElectionResult.Led,
            2 => ElectionResult.SessionEnded,
            _ => throw RespReplies.Unexpected(reply)
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> RenewAsync(
        Leadership leadership,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        ArgumentNullException.ThrowIfNull(timeToLive);
        RespCall.Check(options, cancellationToken);

        var keyspace = leadership.Leader.Role.Keyspace;
        var roleId = RespKeys.Name(leadership.Leader.Role);
        var ttlMs = RespCall.ToMilliseconds(timeToLive);

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "elections"),
            RespKeys.ForKeyspace(keyspace, "election-fencing")
        };

        var arguments = new RedisValue[]
        {
            "renew",
            roleId,
            "",  // value (unused on renew)
            leadership.Owner.Value,
            "",  // sessionId (unused on renew)
            ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Election, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status == 0;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        RespCall.Check(options, cancellationToken);

        var keyspace = leadership.Leader.Role.Keyspace;
        var roleId = RespKeys.Name(leadership.Leader.Role);

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "elections"),
            RespKeys.ForKeyspace(keyspace, "election-fencing")
        };

        var arguments = new RedisValue[]
        {
            "resign",
            roleId,
            "",  // value (unused on resign)
            leadership.Owner.Value,
            "",  // sessionId (unused on resign)
            "0"  // ttl (unused on resign)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Election, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status == 0;
    }

    /// <inheritdoc />
    public async ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        RespCall.Check(options, cancellationToken);

        var keyspace = role.Keyspace;
        var roleId = RespKeys.Name(role);

        var keys = new[] {
            RespKeys.ForKeyspace(keyspace, "elections"),
            RespKeys.ForKeyspace(keyspace, "election-fencing")
        };

        var arguments = new RedisValue[]
        {
            "read-leader",
            roleId,
            "",  // value (unused on read)
            "",  // ownerToken (unused on read)
            "",  // sessionId (unused on read)
            "0"  // ttl (unused on read)
        };

        var reply = await Server
            .EvaluateAsync(RespScript.Election, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        if (status != 0)
            throw RespReplies.Unexpected(reply);

        if (fields.Length < 2)
            return new ElectionView(role, null);

        var term = int.Parse((string?)fields[1] ?? throw RespReplies.Unexpected(reply), System.Globalization.CultureInfo.InvariantCulture);
        var value = fields.Length > 2 ? (byte[]?)fields[2] : null;

        if (term == 0)
            return new ElectionView(role, null);

        return new ElectionView(role, new Leader(
            role,
            new FencingToken(term),
            value ?? []
        ));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ElectionView> WatchAsync(
        CoordinationKey role,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        RespCall.Check(options, cancellationToken);

        // For now, return the current view once (no watch support)
        yield return await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
    }
}
