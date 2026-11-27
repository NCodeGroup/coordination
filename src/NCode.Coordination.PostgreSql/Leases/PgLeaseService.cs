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
    /// <summary>The kind of the grant rows of leases.</summary>
    private const short Kind = 1;

    /// <summary>The slot that a lease's grant holds.</summary>
    private const int Slot = 0;

    /// <summary>The capacity that a lease's grant records.</summary>
    private const int Capacity = 1;

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

        return await Grants
            .Table.Keyspaces.RunAsync<LeaseAcquireResult>(
                resource.Keyspace,
                async (id, token) =>
                    await Grants
                        .Table.Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Grants
                                    .BeginAsync(
                                        transaction,
                                        id,
                                        Kind,
                                        resource,
                                        sessionId,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions(sessionId);

                                var now = start.Change.Now;
                                var live = start.Rows.FirstOrDefault(row =>
                                    row.Slot == Slot && row.StateAt(now) == PgRecordState.Live
                                );

                                LeaseAcquireResult result;
                                if (
                                    terms.IsBoundToSession
                                    && (
                                        start.SessionDeadline is not { } deadline || deadline <= now
                                    )
                                )
                                {
                                    result = LeaseAcquireResult.SessionEnded;
                                }
                                else if (live is not null)
                                {
                                    result =
                                        sessionId is not null
                                        && string.Equals(
                                            live.Session,
                                            sessionId,
                                            StringComparison.Ordinal
                                        )
                                            ? LeaseAcquireResult.Acquired(
                                                new LeaseGrant(
                                                    resource,
                                                    new FencingToken(live.Fencing),
                                                    new OwnerToken(live.Owner),
                                                    terms
                                                )
                                            )
                                            : LeaseAcquireResult.Held;
                                }
                                else
                                {
                                    var fencing = start.Last + 1;
                                    var owner = Tokens.Next(PgTokenPrefixes.Owner);
                                    DateTime? grantDeadline = terms.IsBoundToSession
                                        ? null
                                        : now.AddTicks(
                                            PgCall.ToMicroseconds(terms.TimeToLive) * 10
                                        );

                                    start.Change.Write(
                                        Grants.Upsert(
                                            id,
                                            Kind,
                                            resource,
                                            Slot,
                                            Capacity,
                                            fencing,
                                            owner,
                                            sessionId,
                                            grantDeadline,
                                            null
                                        )
                                    );
                                    start.Change.Write(Grants.Fence(id, Kind, resource, fencing));

                                    result = LeaseAcquireResult.Acquired(
                                        new LeaseGrant(
                                            resource,
                                            new FencingToken(fencing),
                                            new OwnerToken(owner),
                                            terms
                                        )
                                    );
                                }

                                await Grants
                                    .Table.CommitAsync(
                                        transaction,
                                        start.Change,
                                        id,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);
                                return result;
                            },
                            token
                        )
                        .ConfigureAwait(false),
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

        var microseconds = PgCall.ToMicroseconds(timeToLive);

        return await ChangeHeldAsync(
                grant,
                (id, row, now) =>
                    Grants.Renew(
                        id,
                        Kind,
                        grant.Resource,
                        Slot,
                        row.Fencing,
                        now.AddTicks(microseconds * 10)
                    ),
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
        return await Grants
            .Table.Keyspaces.RunAsync<bool>(
                grant.Resource.Keyspace,
                async (id, token) =>
                    await Grants
                        .Table.Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Grants
                                    .BeginAsync(
                                        transaction,
                                        id,
                                        Kind,
                                        grant.Resource,
                                        null,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions(null);

                                var now = start.Change.Now;
                                var held = start.Rows.FirstOrDefault(row =>
                                    row.Slot == Slot
                                    && row.Fencing == grant.FencingToken.Value
                                    && string.Equals(
                                        row.Owner,
                                        grant.Owner.Value,
                                        StringComparison.Ordinal
                                    )
                                    && row.StateAt(now) == PgRecordState.Live
                                );

                                if (held is not null)
                                {
                                    start.Change.Write(write(id, held, now));
                                }

                                await Grants
                                    .Table.CommitAsync(
                                        transaction,
                                        start.Change,
                                        id,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);
                                return held is not null;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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
                async (id, token) =>
                    (
                        await Grants.ReadAsync(id, Kind, resource, token).ConfigureAwait(false)
                    ).Length,
                cancellationToken
            )
            .ConfigureAwait(false);

        return live == 0;
    }
}
