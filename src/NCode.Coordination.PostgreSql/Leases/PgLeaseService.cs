// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The leases of the PostgreSQL provider: each lease is a row of the <c>grants</c> table of kind 1 and slot 0, and the
/// resource's fencing tokens are kept in the <c>fencing</c> table, so a grant after a sweep still gets a greater one.
/// Every change of a grant is a transaction that locks the keyspace's row, ends the expired sessions it meets and
/// decides from the rows it read; a read is one statement, read again after the keyspace's writers when it meets an
/// expired grant. A grant takes no revision and logs no entry. A wait for a release polls until no grant is live.
/// </summary>
/// <param name="grants">The reads and writes that the grants share.</param>
/// <param name="tokens">The source of owner tokens.</param>
/// <param name="polling">The poll that a wait for a release runs on the provider's clock.</param>
/// <exception cref="ArgumentNullException">The grants, the tokens or the polling are null.</exception>
internal sealed class PgLeaseService(PgGrants grants, IPgTokens tokens, PgPolling polling)
    : ILeaseService
{
    /// <summary>
    /// The slot that a lease's grant holds.
    /// </summary>
    private const int Slot = 0;

    /// <summary>
    /// The capacity that a lease's grant records.
    /// </summary>
    private const int Capacity = 1;

    /// <summary>
    /// Builds the grant that a lease's grant stands for, as the grants' writes name it.
    /// </summary>
    /// <param name="grant">The lease's grant.</param>
    /// <returns>The held grant.</returns>
    private static PgHeldGrant HeldOf(LeaseGrant grant) =>
        new(PgGrantKinds.Lease, grant.Resource, Slot, grant.FencingToken.Value, grant.Owner.Value);

    /// <summary>
    /// Decides an attempt to acquire a lease that a live grant holds: the attempt's own session is the holder's when
    /// the grant is bound to it.
    /// </summary>
    /// <param name="live">The live grant row of the lease.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result that says the lease is held by another, or that the attempt's session already holds it.</returns>
    private static LeaseAcquireResult DecideHeld(PgGrantRow live, PgLeaseRequest request)
    {
        if (!live.IsBoundTo(request.SessionId))
        {
            return LeaseAcquireResult.Held;
        }

        var fencingToken = new FencingToken(live.Fencing);
        var ownerToken = new OwnerToken(live.Owner);
        var grant = new LeaseGrant(request.Resource, fencingToken, ownerToken, request.Terms);
        return LeaseAcquireResult.Acquired(grant);
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
            .RunInKeyspaceAsync<LeaseAcquireResult>(
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

        var held = HeldOf(grant);

        return await Grants
            .RenewHeldAsync(held, timeToLive, cancellationToken)
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
            .UntilAsync(token => IsFreeAsync(resource, token), cancellationToken)
            .ConfigureAwait(false);
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
            .TransactAsync(
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
        var scope = new PgGrantScope(id, PgGrantKinds.Lease, request.Resource);

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
    /// Decides an attempt to acquire a lease from the grant rows that were read, having made its writes in the change.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the attempt needs.</param>
    /// <returns>The result of the attempt.</returns>
    private LeaseAcquireResult DecideAcquire(long id, PgGrantStart start, PgLeaseRequest request)
    {
        if (start.SessionEnded(request.SessionId))
        {
            return LeaseAcquireResult.SessionEnded;
        }

        var live = start.LiveIn(Slot);

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
        var owner = Tokens.Next(PgTokenPrefixes.Owner);
        var deadline = PgGrants.DeadlineOf(start.Change.Now, request.Terms);

        var newGrant = new PgNewGrant(PgGrantKinds.Lease, resource, Slot, Capacity, owner)
        {
            Session = request.SessionId,
            Deadline = deadline,
        };
        var fencing = Grants.Grant(start, id, newGrant);

        var fencingToken = new FencingToken(fencing);
        var ownerToken = new OwnerToken(owner);
        var grant = new LeaseGrant(resource, fencingToken, ownerToken, request.Terms);
        return LeaseAcquireResult.Acquired(grant);
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
            .RunInKeyspaceAsync<int>(
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
        var scope = new PgGrantScope(id, PgGrantKinds.Lease, resource);
        var live = await Grants.ReadAsync(scope, cancellationToken).ConfigureAwait(false);

        return live.Length;
    }
}
