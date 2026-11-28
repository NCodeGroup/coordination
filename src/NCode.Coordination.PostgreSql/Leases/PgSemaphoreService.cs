// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The semaphores of the PostgreSQL provider: each slot is a row of the <c>grants</c> table of kind 2, and the
/// resource's fencing tokens are kept in the <c>fencing</c> table, so a grant after a sweep still gets a greater one.
/// Every operation is a transaction that locks the keyspace's row, ends the expired sessions it meets and decides from
/// the rows it read. A grant takes no revision and logs no entry. A wait for a release polls until a slot is free.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <exception cref="ArgumentNullException">The grants or the tokens are null.</exception>
internal sealed class PgSemaphoreService(PgGrants grants, IPgTokens tokens) : ISemaphoreService
{
    /// <summary>
    /// The kind of the grant rows of semaphores.
    /// </summary>
    private const short Kind = 2;

    /// <summary>
    /// Finds the lowest slot of a semaphore that no live grant holds.
    /// </summary>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="capacity">The semaphore's capacity.</param>
    /// <returns>The lowest free slot, or -1 when every slot is held.</returns>
    private static int LowestFreeSlot(List<PgGrantRow> live, int capacity)
    {
        for (var slot = 0; slot < capacity; slot++)
        {
            if (!live.Exists(row => row.Slot == slot))
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Tells whether the session of a grant's terms has ended: the terms bind to a session whose row is gone or past
    /// its deadline.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="terms">The terms of the grant.</param>
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
    /// Finds the live grant that the attempt's own session already holds.
    /// </summary>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="sessionId">The id of the attempt's session, or null when it has none.</param>
    /// <returns>The row, or null when the session holds no slot.</returns>
    private static PgGrantRow? FindOwnSessionGrant(List<PgGrantRow> live, string? sessionId)
    {
        if (sessionId is null)
        {
            return null;
        }

        return live.Find(row => string.Equals(row.Session, sessionId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Decides an attempt whose session already holds a slot: the grant is the one it holds.
    /// </summary>
    /// <param name="held">The live grant row that the attempt's session holds.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the attempt's session already holds its slot.</returns>
    private static SemaphoreAcquireResult DecideHeld(PgGrantRow held, PgSemaphoreRequest request)
    {
        var fencingToken = new FencingToken(held.Fencing);
        var ownerToken = new OwnerToken(held.Owner);
        var grant = new SemaphoreGrant(
            request.Resource,
            held.Slot,
            fencingToken,
            ownerToken,
            request.Terms
        );
        return SemaphoreAcquireResult.Acquired(grant);
    }

    /// <summary>
    /// Gets the instant a new grant expires.
    /// </summary>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="terms">The terms of the grant.</param>
    /// <returns>The deadline, or null when the grant is bound to a session and lives as it does.</returns>
    private static DateTime? GrantDeadlineOf(DateTime now, LeaseTerms terms)
    {
        if (terms.IsBoundToSession)
        {
            return null;
        }

        return PgCall.DeadlineAfter(now, terms.TimeToLive);
    }

    /// <summary>
    /// Finds the grant row of a semaphore's slot when the grant is still the live one: the slot, the fencing token
    /// and the owner token all match.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="grant">The grant.</param>
    /// <returns>The row, or null when the grant has ended.</returns>
    private static PgGrantRow? FindHeld(PgGrantStart start, SemaphoreGrant grant)
    {
        var now = start.Change.Now;
        var fencing = grant.FencingToken.Value;
        var owner = grant.Owner.Value;

        return start.Rows.FirstOrDefault(row =>
            row.Slot == grant.Slot
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
        PgCall.Check(options, cancellationToken);

        var sessionId = terms.IsBoundToSession ? terms.Session.Value : null;
        var request = new PgSemaphoreRequest(resource, capacity, terms, sessionId);

        return await Grants
            .Table.Keyspaces.RunAsync<SemaphoreAcquireResult>(
                resource.Keyspace,
                (id, token) => AcquireInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
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
                (id, row, now) => RenewStatement(id, grant, row, now, timeToLive),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAsync(
        SemaphoreGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        PgCall.Check(options, cancellationToken);

        return await ChangeHeldAsync(
                grant,
                (id, row, _) => Grants.Delete(id, Kind, grant.Resource, grant.Slot, row.Fencing),
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
            .UntilAsync(token => HasFreeSlotAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the statement that renews the grant of a semaphore's slot.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="grant">The grant.</param>
    /// <param name="row">The slot's grant row.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="timeToLive">The time to live that the grant is renewed for.</param>
    /// <returns>The statement.</returns>
    private PgStatement RenewStatement(
        long id,
        SemaphoreGrant grant,
        PgGrantRow row,
        DateTime now,
        TimeToLive timeToLive
    )
    {
        var deadline = PgCall.DeadlineAfter(now, timeToLive);
        return Grants.Renew(id, Kind, grant.Resource, grant.Slot, row.Fencing, deadline);
    }

    /// <summary>
    /// Attempts to acquire a slot of a semaphore in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<SemaphoreAcquireResult> AcquireInKeyspaceAsync(
        long id,
        PgSemaphoreRequest request,
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
    /// Begins a change, decides the attempt from the semaphore's grant rows and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the attempt.</returns>
    private async ValueTask<SemaphoreAcquireResult> AcquireInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgSemaphoreRequest request,
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
    /// Decides an attempt to acquire a slot from the grant rows that were read, having made its writes in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result of the attempt.</returns>
    private SemaphoreAcquireResult DecideAcquire(
        long id,
        PgGrantStart start,
        PgSemaphoreRequest request
    )
    {
        if (SessionHasEnded(start, request.Terms))
        {
            return SemaphoreAcquireResult.SessionEnded;
        }

        var now = start.Change.Now;
        var live = start.Rows.Where(row => row.StateAt(now) == PgRecordState.Live).ToList();

        if (live.Exists(row => row.Capacity != request.Capacity))
        {
            return SemaphoreAcquireResult.CapacityConflict;
        }

        var held = FindOwnSessionGrant(live, request.SessionId);
        if (held is not null)
        {
            return DecideHeld(held, request);
        }

        return GrantSlot(id, start, live, request);
    }

    /// <summary>
    /// Gives the attempt the lowest free slot: writes its grant and the resource's fencing token in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the semaphore is full, or that the attempt's grant was acquired.</returns>
    private SemaphoreAcquireResult GrantSlot(
        long id,
        PgGrantStart start,
        List<PgGrantRow> live,
        PgSemaphoreRequest request
    )
    {
        var slot = LowestFreeSlot(live, request.Capacity);
        if (slot < 0)
        {
            return SemaphoreAcquireResult.Full;
        }

        var resource = request.Resource;
        var fencing = start.Last + 1;
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = GrantDeadlineOf(start.Change.Now, request.Terms);

        var upsert = Grants.Upsert(
            id,
            Kind,
            resource,
            slot,
            request.Capacity,
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
        var grant = new SemaphoreGrant(resource, slot, fencingToken, ownerToken, request.Terms);
        return SemaphoreAcquireResult.Acquired(grant);
    }

    /// <summary>
    /// Applies a write to the slot of a grant when the grant is still the live one: the slot, the owner token and the
    /// fencing token all match.
    /// </summary>
    /// <param name="grant">The grant to change.</param>
    /// <param name="write">Builds the write from the keyspace's id, the grant's row and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> ChangeHeldAsync(
        SemaphoreGrant grant,
        Func<long, PgGrantRow, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        var change = new PgSemaphoreHeldChange(grant, write);

        return await Grants
            .Table.Keyspaces.RunAsync<bool>(
                grant.Resource.Keyspace,
                (id, token) => ChangeHeldInKeyspaceAsync(id, change, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Changes the grant of a semaphore's slot in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="change">The grant and how to build the write.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    private async ValueTask<bool> ChangeHeldInKeyspaceAsync(
        long id,
        PgSemaphoreHeldChange change,
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
        PgSemaphoreHeldChange change,
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
    /// Asks the server whether a semaphore has a free slot.
    /// </summary>
    /// <param name="resource">The resource that the semaphore guards.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when at least one slot of the semaphore is free.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> HasFreeSlotAsync(
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        var live = await Grants
            .Table.Keyspaces.RunAsync(
                resource.Keyspace,
                (id, token) => Grants.ReadAsync(id, Kind, resource, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        return live.Length == 0 || live.Length < live[0].Capacity;
    }
}
