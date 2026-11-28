// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Elections;

/// <summary>
/// The elections of the PostgreSQL provider: each leadership is a row of the <c>grants</c> table of kind 3 and slot 0
/// that holds the leader's value, and the role's fencing tokens, which are the terms, are kept in the <c>fencing</c>
/// table, so a leadership after a sweep still gets a greater one. Every operation is a transaction that locks the
/// keyspace's row, ends the expired sessions it meets and decides from the rows it read. A leadership takes no revision
/// and logs no entry, so a watch polls the role until the caller cancels.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The grants or the tokens are null.</exception>
internal sealed class PgElectionService(PgGrants grants, IPgTokens tokens) : IElectionService
{
    /// <summary>
    /// The kind of the grant rows of elections.
    /// </summary>
    private const short Kind = 3;

    /// <summary>
    /// The slot that a leadership's grant holds.
    /// </summary>
    private const int Slot = 0;

    /// <summary>
    /// The capacity that a leadership's grant records.
    /// </summary>
    private const int Capacity = 1;

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

    /// <summary>
    /// Tells whether the session of a leadership's terms has ended: the terms bind to a session whose row is gone or
    /// past its deadline.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="terms">The terms of the leadership.</param>
    /// <returns>True when the terms' session is not live.</returns>
    private static bool SessionHasEnded(PgGrantStart start, LeaseTerms terms)
    {
        if (!terms.IsBoundToSession)
        {
            return false;
        }

        return start.SessionDeadline is not { } deadline || deadline <= start.Change.Now;
    }

    /// <summary>
    /// Decides an attempt to lead a role that a live leader holds: the attempt's own session is the leader's when the
    /// grant is bound to it.
    /// </summary>
    /// <param name="live">The live grant row of the role.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the role is led by another, or that the attempt's session already leads it.</returns>
    private static ElectionResult DecideLed(PgGrantRow live, PgLeadRequest request)
    {
        var isOwnSession =
            request.SessionId is not null
            && string.Equals(live.Session, request.SessionId, StringComparison.Ordinal);

        if (!isOwnSession)
        {
            return ElectionResult.Led;
        }

        var fencingToken = new FencingToken(live.Fencing);
        var leader = new Leader(request.Role, fencingToken, live.Value ?? []);
        var ownerToken = new OwnerToken(live.Owner);
        var leadership = new Leadership(leader, ownerToken, request.Terms);
        return ElectionResult.Elected(leadership);
    }

    /// <summary>
    /// Gets the instant a new leadership's grant expires.
    /// </summary>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="terms">The terms of the leadership.</param>
    /// <returns>The deadline, or null when the leadership is bound to a session and lives as it does.</returns>
    private static DateTime? GrantDeadlineOf(DateTime now, LeaseTerms terms)
    {
        if (terms.IsBoundToSession)
        {
            return null;
        }

        return PgCall.DeadlineAfter(now, terms.TimeToLive);
    }

    /// <summary>
    /// Finds the grant row of a leadership when the leadership is still the live one: the slot, the fencing token and
    /// the owner token all match.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="leadership">The leadership.</param>
    /// <returns>The row, or null when the leadership has ended.</returns>
    private static PgGrantRow? FindHeld(PgGrantStart start, Leadership leadership)
    {
        var now = start.Change.Now;
        var fencing = leadership.Leader.Term.Value;
        var owner = leadership.Owner.Value;

        return start.Rows.FirstOrDefault(row =>
            row.Slot == Slot
            && row.Fencing == fencing
            && string.Equals(row.Owner, owner, StringComparison.Ordinal)
            && row.StateAt(now) == PgRecordState.Live
        );
    }

    /// <summary>
    /// Gets the leader that a grant row holds.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="row">The role's grant row, or null when it has none.</param>
    /// <returns>The leader, or null when the role has none.</returns>
    private static Leader? ToLeader(CoordinationKey role, PgGrantRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var fencingToken = new FencingToken(row.Fencing);
        return new Leader(role, fencingToken, row.Value ?? []);
    }

    /// <summary>
    /// Gets the reads and writes that the grants share.
    /// </summary>
    private PgGrants Grants { get; } = grants ?? throw new ArgumentNullException(nameof(grants));

    /// <summary>
    /// Gets the source of owner tokens.
    /// </summary>
    private IPgTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

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
        PgCall.Check(options, cancellationToken);

        var bytes = value.ToArray();
        var sessionId = terms.IsBoundToSession ? terms.Session.Value : null;
        var request = new PgLeadRequest(role, bytes, terms, sessionId);

        return await Grants
            .Table.Keyspaces.RunAsync<ElectionResult>(
                role.Keyspace,
                (id, token) => LeadInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
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
        PgCall.Check(options, cancellationToken);

        if (leadership.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A leadership bound to a session lives as its session does, so it is never renewed.",
                nameof(leadership)
            );
        }

        var role = leadership.Leader.Role;

        return await ChangeHeldAsync(
                leadership,
                (id, row, now) => RenewStatement(id, role, row, now, timeToLive),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        PgCall.Check(options, cancellationToken);

        var role = leadership.Leader.Role;

        return await ChangeHeldAsync(
                leadership,
                (id, row, _) => Grants.Delete(id, Kind, role, Slot, row.Fencing),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        PgCall.Check(options, cancellationToken);

        var rows = await Grants
            .Table.Keyspaces.RunAsync<ImmutableArray<PgGrantRow>>(
                role.Keyspace,
                (id, token) => Grants.ReadAsync(id, Kind, role, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var row = rows.FirstOrDefault(candidate => candidate.Slot == Slot);
        var leader = ToLeader(role, row);

        return new ElectionView(role, leader);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ElectionView> WatchAsync(
        CoordinationKey role,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        PgCall.Check(options, cancellationToken);

        var last = await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
        yield return last;

        while (true)
        {
            await Task.Delay(PgPolling.Interval, cancellationToken).ConfigureAwait(false);

            var view = await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
            if (!Equals(view.Leader?.Term, last.Leader?.Term))
            {
                last = view;
                yield return view;
            }
        }
    }

    /// <summary>
    /// Builds the statement that renews the grant of a leadership.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="role">The role.</param>
    /// <param name="row">The leadership's grant row.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="timeToLive">The time to live that the grant is renewed for.</param>
    /// <returns>The statement.</returns>
    private PgStatement RenewStatement(
        long id,
        CoordinationKey role,
        PgGrantRow row,
        DateTime now,
        TimeToLive timeToLive
    )
    {
        var deadline = PgCall.DeadlineAfter(now, timeToLive);
        return Grants.Renew(id, Kind, role, Slot, row.Fencing, deadline);
    }

    /// <summary>
    /// Attempts to lead a role in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<ElectionResult> LeadInKeyspaceAsync(
        long id,
        PgLeadRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Grants
            .Table.Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => LeadInTransactionAsync(transaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides the attempt from the role's grant rows and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<ElectionResult> LeadInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgLeadRequest request,
        CancellationToken cancellationToken
    )
    {
        var start = await Grants
            .BeginAsync(transaction, id, Kind, request.Role, request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(request.SessionId);

        var result = DecideLead(id, start, request);

        await Grants
            .Table.CommitAsync(transaction, start.Change, id, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Decides an attempt to lead a role from the grant rows that were read, having made its writes in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result of the attempt.</returns>
    private ElectionResult DecideLead(long id, PgGrantStart start, PgLeadRequest request)
    {
        if (SessionHasEnded(start, request.Terms))
        {
            return ElectionResult.SessionEnded;
        }

        var now = start.Change.Now;
        var live = start.Rows.FirstOrDefault(row =>
            row.Slot == Slot && row.StateAt(now) == PgRecordState.Live
        );

        if (live is not null)
        {
            return DecideLed(live, request);
        }

        return GrantLeadership(id, start, request);
    }

    /// <summary>
    /// Gives the role a new leader: writes its grant and the role's fencing token in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the attempt's leadership was elected.</returns>
    private ElectionResult GrantLeadership(long id, PgGrantStart start, PgLeadRequest request)
    {
        var role = request.Role;
        var fencing = start.Last + 1;
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = GrantDeadlineOf(start.Change.Now, request.Terms);

        var upsert = Grants.Upsert(
            id,
            Kind,
            role,
            Slot,
            Capacity,
            fencing,
            owner,
            request.SessionId,
            deadline,
            request.Value
        );
        var fence = Grants.Fence(id, Kind, role, fencing);
        start.Change.Write(upsert);
        start.Change.Write(fence);

        var fencingToken = new FencingToken(fencing);
        var leader = new Leader(role, fencingToken, request.Value);
        var ownerToken = new OwnerToken(owner);
        var leadership = new Leadership(leader, ownerToken, request.Terms);
        return ElectionResult.Elected(leadership);
    }

    /// <summary>
    /// Applies a write to the grant of a leadership when the leadership is still the live one: the owner token and the
    /// fencing token both match.
    /// </summary>
    /// <param name="leadership">The leadership to change.</param>
    /// <param name="write">Builds the write from the keyspace's id, the grant's row and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the leadership has ended.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> ChangeHeldAsync(
        Leadership leadership,
        Func<long, PgGrantRow, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        var change = new PgHeldChange(leadership, write);

        return await Grants
            .Table.Keyspaces.RunAsync<bool>(
                leadership.Leader.Role.Keyspace,
                (id, token) => ChangeHeldInKeyspaceAsync(id, change, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Changes the grant of a leadership in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="change">The leadership and how to build the write.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the leadership has ended.</returns>
    private async ValueTask<bool> ChangeHeldInKeyspaceAsync(
        long id,
        PgHeldChange change,
        CancellationToken cancellationToken
    )
    {
        return await Grants
            .Table.Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) =>
                    ChangeHeldInTransactionAsync(transaction, id, change, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, makes the write when the leadership is still the live one and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="change">The leadership and how to build the write.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the leadership has ended.</returns>
    private async ValueTask<bool> ChangeHeldInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgHeldChange change,
        CancellationToken cancellationToken
    )
    {
        var role = change.Leadership.Leader.Role;
        var start = await Grants
            .BeginAsync(transaction, id, Kind, role, null, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(null);

        var held = FindHeld(start, change.Leadership);
        if (held is not null)
        {
            var statement = change.Write(id, held, start.Change.Now);
            start.Change.Write(statement);
        }

        await Grants
            .Table.CommitAsync(transaction, start.Change, id, cancellationToken)
            .ConfigureAwait(false);
        return held is not null;
    }
}
