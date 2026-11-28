// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Elections;

/// <summary>
/// The elections of the PostgreSQL provider: each leadership is a row of the <c>grants</c> table of kind 3 and slot 0
/// that holds the leader's value, and the role's fencing tokens, which are the terms, are kept in the <c>fencing</c>
/// table, so a leadership after a sweep still gets a greater one. Every change of a leadership is a transaction that
/// locks the keyspace's row, ends the expired sessions it meets and decides from the rows it read; a read is one
/// statement, read again after the keyspace's writers when it meets an expired grant. A leadership takes no revision
/// and logs no entry, though ending an expired session it meets does, so a watch polls the role until the caller
/// cancels.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <param name="polling">The poll that a watch runs on the provider's clock.</param>
/// <exception cref="ArgumentNullException">The grants, the tokens or the polling are null.</exception>
internal sealed class PgElectionService(PgGrants grants, IPgTokens tokens, PgPolling polling)
    : IElectionService
{
    /// <summary>
    /// Builds the grant that a leadership stands for, as the grants' writes name it.
    /// </summary>
    /// <param name="leadership">The leadership.</param>
    /// <returns>The held grant.</returns>
    private static PgHeldGrant HeldOf(Leadership leadership) =>
        new(
            PgGrantKinds.Election,
            leadership.Leader.Role,
            PgGrants.SingleHolderSlot,
            leadership.Leader.Term.Value,
            leadership.Owner.Value
        );

    /// <summary>
    /// Finds the grant row that holds a role's leadership.
    /// </summary>
    /// <param name="rows">The live grants of the role.</param>
    /// <returns>The row of the leadership's slot, or null when the role has none.</returns>
    private static PgGrantRow? RowOfSlot(ImmutableArray<PgGrantRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.Slot == PgGrants.SingleHolderSlot)
            {
                return row;
            }
        }

        return null;
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
        if (!live.IsBoundTo(request.SessionId))
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

    /// <summary>
    /// Gets the poll that a watch runs on the provider's clock.
    /// </summary>
    private PgPolling Polling { get; } =
        polling ?? throw new ArgumentNullException(nameof(polling));

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
        PgCall.CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);

        var bytes = value.ToArray();
        var sessionId = terms.IsBoundToSession ? terms.Session.Value : null;
        var request = new PgLeadRequest(role, bytes, terms, sessionId);

        return await Grants
            .RunInKeyspaceAsync<ElectionResult>(
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

        var held = HeldOf(leadership);

        return await Grants
            .RenewHeldAsync(held, timeToLive, cancellationToken)
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

        var held = HeldOf(leadership);

        return await Grants.ReleaseHeldAsync(held, cancellationToken).ConfigureAwait(false);
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
            .RunInKeyspaceAsync<ImmutableArray<PgGrantRow>>(
                role.Keyspace,
                (id, token) => ReadRowsAsync(id, role, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var row = RowOfSlot(rows);
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
            await Polling.DelayAsync(cancellationToken).ConfigureAwait(false);

            var view = await ReadAsync(role, options, cancellationToken).ConfigureAwait(false);
            if (!Equals(view.Leader?.Term, last.Leader?.Term))
            {
                last = view;
                yield return view;
            }
        }
    }

    /// <summary>
    /// Reads the live grants of a role in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The live grants, by slot.</returns>
    private ValueTask<ImmutableArray<PgGrantRow>> ReadRowsAsync(
        long id,
        CoordinationKey role,
        CancellationToken cancellationToken
    )
    {
        var scope = new PgGrantScope(id, PgGrantKinds.Election, role);

        return Grants.ReadAsync(scope, cancellationToken);
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
            .TransactAsync(
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
        var scope = new PgGrantScope(id, PgGrantKinds.Election, request.Role);

        var start = await Grants
            .BeginAsync(transaction, scope, request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(request.SessionId);

        var result = DecideLead(id, start, request);

        await Grants
            .CommitAsync(transaction, start.Change, id, cancellationToken)
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
        if (start.SessionEnded(request.SessionId))
        {
            return ElectionResult.SessionEnded;
        }

        var live = start.LiveIn(PgGrants.SingleHolderSlot);

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
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = PgGrants.DeadlineOf(start.Change.Now, request.Terms);

        var newGrant = new PgNewGrant(
            PgGrantKinds.Election,
            role,
            PgGrants.SingleHolderSlot,
            PgGrants.SingleHolderCapacity,
            owner
        )
        {
            Session = request.SessionId,
            Deadline = deadline,
            Value = request.Value,
        };
        var fencing = Grants.Grant(start, id, newGrant);

        var fencingToken = new FencingToken(fencing);
        var leader = new Leader(role, fencingToken, request.Value);
        var ownerToken = new OwnerToken(owner);
        var leadership = new Leadership(leader, ownerToken, request.Terms);
        return ElectionResult.Elected(leadership);
    }
}
