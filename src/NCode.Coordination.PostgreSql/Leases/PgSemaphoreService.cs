// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The semaphores of the PostgreSQL provider: each slot is a row of the <c>grants</c> table of kind 2, and the
/// resource's fencing tokens are kept in the <c>fencing</c> table, so a grant after a sweep still gets a greater one.
/// Every change of a grant is a transaction that locks the keyspace's row, ends the expired sessions it meets and
/// decides from the rows it read; a read is one statement, read again after the keyspace's writers when it meets an
/// expired grant. A grant takes no revision and logs no entry. A wait for a release polls until a slot is free.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <param name="polling">The poll that a wait for a release runs on the provider's clock.</param>
/// <exception cref="ArgumentNullException">The grants, the tokens or the polling are null.</exception>
internal sealed class PgSemaphoreService(PgGrants grants, IPgTokens tokens, PgPolling polling)
    : ISemaphoreService
{
    /// <summary>
    /// Builds the grant that a semaphore's grant stands for, as the grants' writes name it.
    /// </summary>
    /// <param name="grant">The semaphore grant.</param>
    /// <returns>The held grant.</returns>
    private static PgHeldGrant HeldOf(SemaphoreGrant grant) =>
        new(
            PgGrantKinds.Semaphore,
            grant.Resource,
            grant.Slot,
            grant.FencingToken.Value,
            grant.Owner.Value
        );

    /// <summary>
    /// Collects the grant rows of a read that are live at the instant of the read.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <returns>The live rows, in slot order.</returns>
    private static List<PgGrantRow> LiveRowsOf(PgGrantStart start)
    {
        var now = start.Change.Now;
        var live = new List<PgGrantRow>(start.Rows.Length);

        foreach (var row in start.Rows)
        {
            if (row.StateAt(now) == PgRecordState.Live)
            {
                live.Add(row);
            }
        }

        return live;
    }

    /// <summary>
    /// Tells whether a live grant of the semaphore was made with a capacity other than the request's.
    /// </summary>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="capacity">The capacity that the request names.</param>
    /// <returns><see langword="true"/> when a live grant's capacity differs.</returns>
    private static bool CapacityDiffers(IReadOnlyList<PgGrantRow> live, int capacity)
    {
        foreach (var row in live)
        {
            if (row.Capacity != capacity)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the lowest slot of a semaphore that no live grant holds.
    /// </summary>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="capacity">The semaphore's capacity.</param>
    /// <returns>The lowest free slot, or -1 when every slot is held.</returns>
    private static int LowestFreeSlot(IReadOnlyList<PgGrantRow> live, int capacity)
    {
        var taken = new bool[capacity];

        foreach (var row in live)
        {
            if (row.Slot >= 0 && row.Slot < capacity)
            {
                taken[row.Slot] = true;
            }
        }

        return Array.IndexOf(taken, false);
    }

    /// <summary>
    /// Finds the live grant that the attempt's own session already holds.
    /// </summary>
    /// <param name="live">The live grants of the semaphore.</param>
    /// <param name="sessionId">The id of the attempt's session, or null when it has none.</param>
    /// <returns>The row, or null when the session holds no slot.</returns>
    private static PgGrantRow? FindOwnSessionGrant(
        IReadOnlyList<PgGrantRow> live,
        string? sessionId
    )
    {
        if (sessionId is null)
        {
            return null;
        }

        foreach (var row in live)
        {
            if (string.Equals(row.Session, sessionId, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
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
    /// Gets the reads and writes that the grants share.
    /// </summary>
    private PgGrants Grants { get; } = grants ?? throw new ArgumentNullException(nameof(grants));

    /// <summary>
    /// Gets the source of owner tokens.
    /// </summary>
    private IPgTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <summary>
    /// Gets the poll that a wait for a release runs on the provider's clock.
    /// </summary>
    private PgPolling Polling { get; } =
        polling ?? throw new ArgumentNullException(nameof(polling));

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
            .RunInKeyspaceAsync<SemaphoreAcquireResult>(
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

        var held = HeldOf(grant);

        return await Grants
            .RenewHeldAsync(held, timeToLive, cancellationToken)
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

        var held = HeldOf(grant);

        return await Grants.ReleaseHeldAsync(held, cancellationToken).ConfigureAwait(false);
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

        await Polling
            .UntilAsync(token => HasFreeSlotAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
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
            .TransactAsync(
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
        var scope = new PgGrantScope(id, PgGrantKinds.Semaphore, request.Resource);

        var start = await Grants
            .BeginAsync(transaction, scope, request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(request.SessionId);

        var result = DecideAcquire(id, start, request);

        await Grants
            .CommitAsync(transaction, start.Change, id, cancellationToken)
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
        if (start.SessionEnded(request.SessionId))
        {
            return SemaphoreAcquireResult.SessionEnded;
        }

        var live = LiveRowsOf(start);

        if (CapacityDiffers(live, request.Capacity))
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
        IReadOnlyList<PgGrantRow> live,
        PgSemaphoreRequest request
    )
    {
        var slot = LowestFreeSlot(live, request.Capacity);
        if (slot < 0)
        {
            return SemaphoreAcquireResult.Full;
        }

        var resource = request.Resource;
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = PgGrants.DeadlineOf(start.Change.Now, request.Terms);

        var newGrant = new PgNewGrant(
            PgGrantKinds.Semaphore,
            resource,
            slot,
            request.Capacity,
            owner
        )
        {
            Session = request.SessionId,
            Deadline = deadline,
        };
        var fencing = Grants.Grant(start, id, newGrant);

        var fencingToken = new FencingToken(fencing);
        var ownerToken = new OwnerToken(owner);
        var grant = new SemaphoreGrant(resource, slot, fencingToken, ownerToken, request.Terms);
        return SemaphoreAcquireResult.Acquired(grant);
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
            .RunInKeyspaceAsync(
                resource.Keyspace,
                (id, token) => ReadLiveAsync(id, resource, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        return live.Length == 0 || live.Length < live[0].Capacity;
    }

    /// <summary>
    /// Reads the live grants of a semaphore's resource in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="resource">The resource that the semaphore guards.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The live grants, by slot.</returns>
    private ValueTask<ImmutableArray<PgGrantRow>> ReadLiveAsync(
        long id,
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        var scope = new PgGrantScope(id, PgGrantKinds.Semaphore, resource);

        return Grants.ReadAsync(scope, cancellationToken);
    }
}
