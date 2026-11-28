// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The leases of the PostgreSQL provider: each lease is a row of the <c>grants</c> table of kind 1 and slot 0, and the
/// resource's fencing tokens are kept in the <c>fencing</c> table, so a grant after a sweep still gets a greater one.
/// Every operation is a transaction that locks the keyspace's row, ends the expired sessions it meets and decides from
/// the rows it read. A grant takes no revision and logs no entry. A wait for a release polls until no grant is live.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The grants or the tokens are null.</exception>
internal sealed class PgLeaseService(PgGrants grants, IPgTokens tokens) : ILeaseService
{
    /// <summary>
    /// The kind of the grant rows of leases.
    /// </summary>
    private const short Kind = 1;

    /// <summary>
    /// The slot that a lease's grant holds.
    /// </summary>
    private const int Slot = 0;

    /// <summary>
    /// The capacity that a lease's grant records.
    /// </summary>
    private const int Capacity = 1;

    /// <summary>
    /// Tells whether the session of a lease's terms has ended: the terms bind to a session whose row is gone or past
    /// its deadline.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="terms">The terms of the lease.</param>
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
    /// Decides an attempt to acquire a lease that a live grant holds: the attempt's own session is the holder's when
    /// the grant is bound to it.
    /// </summary>
    /// <param name="live">The live grant row of the lease.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the lease is held by another, or that the attempt's session already holds it.</returns>
    private static LeaseAcquireResult DecideHeld(PgGrantRow live, PgLeaseRequest request)
    {
        var isOwnSession =
            request.SessionId is not null
            && string.Equals(live.Session, request.SessionId, StringComparison.Ordinal);

        if (!isOwnSession)
        {
            return LeaseAcquireResult.Held;
        }

        var fencingToken = new FencingToken(live.Fencing);
        var ownerToken = new OwnerToken(live.Owner);
        var grant = new LeaseGrant(request.Resource, fencingToken, ownerToken, request.Terms);
        return LeaseAcquireResult.Acquired(grant);
    }

    /// <summary>
    /// Gets the instant a new lease's grant expires.
    /// </summary>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="terms">The terms of the lease.</param>
    /// <returns>The deadline, or null when the lease is bound to a session and lives as it does.</returns>
    private static DateTime? GrantDeadlineOf(DateTime now, LeaseTerms terms)
    {
        if (terms.IsBoundToSession)
        {
            return null;
        }

        return PgCall.DeadlineAfter(now, terms.TimeToLive);
    }

    /// <summary>
    /// Finds the grant row of a lease when the grant is still the live one: the slot, the fencing token and the owner
    /// token all match.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="grant">The grant.</param>
    /// <returns>The row, or null when the grant has ended.</returns>
    private static PgGrantRow? FindHeld(PgGrantStart start, LeaseGrant grant)
    {
        var now = start.Change.Now;
        var fencing = grant.FencingToken.Value;
        var owner = grant.Owner.Value;

        return start.Rows.FirstOrDefault(row =>
            row.Slot == Slot
            && row.Fencing == fencing
            && string.Equals(row.Owner, owner, StringComparison.Ordinal)
            && row.StateAt(now) == PgRecordState.Live
        );
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
    public async ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);
        PgCall.Check(options, cancellationToken);

        var sessionId = terms.IsBoundToSession ? terms.Session.Value : null;
        var request = new PgLeaseRequest(resource, terms, sessionId);

        return await Grants
            .Table.Keyspaces.RunAsync<LeaseAcquireResult>(
                resource.Keyspace,
                (id, token) => AcquireInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
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
        PgCall.Check(options, cancellationToken);

        if (grant.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A grant bound to a session lives as its session does, so it is never renewed.",
                nameof(grant)
            );
        }

        return await ChangeHeldAsync(
                grant,
                (id, row, now) => RenewStatement(id, grant.Resource, row, now, timeToLive),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        PgCall.Check(options, cancellationToken);

        return await ChangeHeldAsync(
                grant,
                (id, row, _) => Grants.Delete(id, Kind, grant.Resource, Slot, row.Fencing),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        PgCall.Check(options, cancellationToken);

        await PgPolling
            .UntilAsync(token => IsFreeAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the statement that renews the grant of a lease.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="resource">The resource that the lease guards.</param>
    /// <param name="row">The lease's grant row.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="timeToLive">The time to live that the grant is renewed for.</param>
    /// <returns>The statement.</returns>
    private PgStatement RenewStatement(
        long id,
        CoordinationKey resource,
        PgGrantRow row,
        DateTime now,
        TimeToLive timeToLive
    )
    {
        var deadline = PgCall.DeadlineAfter(now, timeToLive);
        return Grants.Renew(id, Kind, resource, Slot, row.Fencing, deadline);
    }

    /// <summary>
    /// Attempts to acquire a lease in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<LeaseAcquireResult> AcquireInKeyspaceAsync(
        long id,
        PgLeaseRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Grants
            .Table.Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => AcquireInTransactionAsync(transaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides the attempt from the lease's grant rows and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<LeaseAcquireResult> AcquireInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgLeaseRequest request,
        CancellationToken cancellationToken
    )
    {
        var start = await Grants
            .BeginAsync(
                transaction,
                id,
                Kind,
                request.Resource,
                request.SessionId,
                cancellationToken
            )
            .ConfigureAwait(false);

        start.EndExpiredSessions(request.SessionId);

        var result = DecideAcquire(id, start, request);

        await Grants
            .Table.CommitAsync(transaction, start.Change, id, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Decides an attempt to acquire a lease from the grant rows that were read, having made its writes in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result of the attempt.</returns>
    private LeaseAcquireResult DecideAcquire(long id, PgGrantStart start, PgLeaseRequest request)
    {
        if (SessionHasEnded(start, request.Terms))
        {
            return LeaseAcquireResult.SessionEnded;
        }

        var now = start.Change.Now;
        var live = start.Rows.FirstOrDefault(row =>
            row.Slot == Slot && row.StateAt(now) == PgRecordState.Live
        );

        if (live is not null)
        {
            return DecideHeld(live, request);
        }

        return GrantLease(id, start, request);
    }

    /// <summary>
    /// Gives the lease a new holder: writes its grant and the resource's fencing token in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the attempt's grant was acquired.</returns>
    private LeaseAcquireResult GrantLease(long id, PgGrantStart start, PgLeaseRequest request)
    {
        var resource = request.Resource;
        var fencing = start.Last + 1;
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = GrantDeadlineOf(start.Change.Now, request.Terms);

        var upsert = Grants.Upsert(
            id,
            Kind,
            resource,
            Slot,
            Capacity,
            fencing,
            owner,
            request.SessionId,
            deadline,
            null
        );
        var fence = Grants.Fence(id, Kind, resource, fencing);
        start.Change.Write(upsert);
        start.Change.Write(fence);

        var fencingToken = new FencingToken(fencing);
        var ownerToken = new OwnerToken(owner);
        var grant = new LeaseGrant(resource, fencingToken, ownerToken, request.Terms);
        return LeaseAcquireResult.Acquired(grant);
    }

    /// <summary>
    /// Applies a write to the lease of a grant when the grant is still the live one: the slot, the owner token and the
    /// fencing token all match.
    /// </summary>
    /// <param name="grant">The grant to change.</param>
    /// <param name="write">Builds the write from the keyspace's id, the grant's row and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> ChangeHeldAsync(
        LeaseGrant grant,
        Func<long, PgGrantRow, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        var change = new PgLeaseHeldChange(grant, write);

        return await Grants
            .Table.Keyspaces.RunAsync<bool>(
                grant.Resource.Keyspace,
                (id, token) => ChangeHeldInKeyspaceAsync(id, change, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Changes the grant of a lease in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="change">The grant and how to build the write.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    private async ValueTask<bool> ChangeHeldInKeyspaceAsync(
        long id,
        PgLeaseHeldChange change,
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
    /// Begins a change, makes the write when the grant is still the live one and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="change">The grant and how to build the write.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    private async ValueTask<bool> ChangeHeldInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgLeaseHeldChange change,
        CancellationToken cancellationToken
    )
    {
        var resource = change.Grant.Resource;
        var start = await Grants
            .BeginAsync(transaction, id, Kind, resource, null, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(null);

        var held = FindHeld(start, change.Grant);
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

    /// <summary>
    /// Asks the server whether a lease is free.
    /// </summary>
    /// <param name="resource">The resource that the lease guards.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when no grant of the resource is live.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> IsFreeAsync(
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        var live = await Grants
            .Table.Keyspaces.RunAsync<int>(
                resource.Keyspace,
                (id, token) => CountLiveAsync(id, resource, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        return live == 0;
    }

    /// <summary>
    /// Counts the live grants of a lease's resource in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="resource">The resource that the lease guards.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The number of live grants.</returns>
    private async ValueTask<int> CountLiveAsync(
        long id,
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        var live = await Grants
            .ReadAsync(id, Kind, resource, cancellationToken)
            .ConfigureAwait(false);

        return live.Length;
    }
}
