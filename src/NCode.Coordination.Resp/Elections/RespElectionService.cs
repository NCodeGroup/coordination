// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Elections;

/// <summary>
/// The elections of the RESP provider: each leadership is a grant in the keyspace's <c>grants</c> hash, a leadership
/// bound to a session lives as its session does and a leadership with a time to live has a deadline in
/// <c>grant-deadlines</c>. One keyspace script serves each operation, every leadership has its own owner token, and a
/// watch polls the server until the caller cancels.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The table or the tokens are null.</exception>
internal class RespElectionService(RespRecordTable table, IRespTokens tokens) : IElectionService
{
    /// <summary>
    /// The text a leadership's grant field starts with.
    /// </summary>
    private const string FieldPrefix = "e ";

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
        CheckValue(value, nameof(value));
        RespCall.Check(options, cancellationToken);

        var name = RespKeys.Name(role);
        var bytes = value.ToArray();
        var owner = Tokens.Next(RespTokenPrefixes.Owner);
        var session = terms.IsBoundToSession ? terms.Session.Value : "";
        var milliseconds = terms.IsBoundToSession ? 0 : RespCall.ToMilliseconds(terms.TimeToLive);

        RedisValue[] arguments = [name, bytes, owner, session, milliseconds];
        var reply = await Table
            .EvaluateAsync(RespScript.ElectionLead, role.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 3 => ToElected(role, terms, fields),
            1 when fields.Length == 0 => ElectionResult.Led,
            2 when fields.Length == 0 => ElectionResult.SessionEnded,
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <summary>
    /// Builds the result of a leadership that the server granted.
    /// </summary>
    /// <param name="role">The role that the leader fills.</param>
    /// <param name="terms">The terms of the leadership.</param>
    /// <param name="fields">The fields of the reply after its status: owner token, fencing token and value.</param>
    /// <returns>The result that carries the leadership.</returns>
    /// <exception cref="CoordinationException">The owner token is missing from the reply.</exception>
    private static ElectionResult ToElected(
        CoordinationKey role,
        LeaseTerms terms,
        RedisResult[] fields
    )
    {
        var fencingValue = RespReplies.Integer(fields[1]);
        var fencing = new FencingToken(fencingValue);
        var value = (byte[]?)fields[2] ?? [];
        var leader = new Leader(role, fencing, value);
        var ownerValue = RespReplies.Text(fields[0]);
        var owner = new OwnerToken(ownerValue);
        var leadership = new Leadership(leader, owner, terms);
        var elected = ElectionResult.Elected(leadership);
        return elected;
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

        if (leadership.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A leadership bound to a session lives as its session does, so it is never renewed.",
                nameof(leadership)
            );
        }

        var role = leadership.Leader.Role;
        var field = FieldPrefix + RespKeys.Name(role);
        var milliseconds = RespCall.ToMilliseconds(timeToLive);

        RedisValue[] arguments = [field, leadership.Owner.Value, milliseconds];
        var reply = await Table
            .EvaluateAsync(RespScript.GrantRenew, role.Keyspace, arguments, cancellationToken)
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
    public async ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        RespCall.Check(options, cancellationToken);

        var role = leadership.Leader.Role;
        var field = FieldPrefix + RespKeys.Name(role);

        RedisValue[] arguments = [field, leadership.Owner.Value];
        var reply = await Table
            .EvaluateAsync(RespScript.GrantRelease, role.Keyspace, arguments, cancellationToken)
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
    public async ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        RespCall.Check(options, cancellationToken);

        var name = RespKeys.Name(role);

        RedisValue[] arguments = [name];
        var reply = await Table
            .EvaluateAsync(RespScript.ElectionRead, role.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 2 => ToView(role, fields),
            1 when fields.Length == 0 => new ElectionView(role, null),
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <summary>
    /// Builds the view of a role that has a leader.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="fields">The fields of the reply after its status: fencing token and value.</param>
    /// <returns>The view that carries the leader.</returns>
    private static ElectionView ToView(CoordinationKey role, RedisResult[] fields)
    {
        var fencingValue = RespReplies.Integer(fields[0]);
        var fencing = new FencingToken(fencingValue);
        var value = (byte[]?)fields[1] ?? [];
        var leader = new Leader(role, fencing, value);
        var view = new ElectionView(role, leader);
        return view;
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

        var last = await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
        yield return last;

        while (true)
        {
            await Task.Delay(RespPolling.Interval, cancellationToken).ConfigureAwait(false);

            var view = await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
            if (!Equals(view.Leader?.Term, last.Leader?.Term))
            {
                last = view;
                yield return view;
            }
        }
    }

    /// <summary>
    /// Checks that a value is no longer than the largest value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="paramName">The name of the parameter that holds the value.</param>
    /// <exception cref="ArgumentException">The value is longer than <see cref="CoordinationLimits.MaxValueLength"/> bytes.</exception>
    private static void CheckValue(ReadOnlyMemory<byte> value, string paramName)
    {
        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                paramName
            );
        }
    }
}
