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
    /// <summary>The kind of the grant rows of elections.</summary>
    private const short Kind = 3;

    /// <summary>The slot that a leadership's grant holds.</summary>
    private const int Slot = 0;

    /// <summary>The capacity that a leadership's grant records.</summary>
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

        return await Grants
            .Table.Keyspaces.RunAsync<ElectionResult>(
                role.Keyspace,
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
                                        role,
                                        sessionId,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions(sessionId);

                                var now = start.Change.Now;
                                var live = start.Rows.FirstOrDefault(row =>
                                    row.Slot == Slot && row.StateAt(now) == PgRecordState.Live
                                );

                                ElectionResult result;
                                if (
                                    terms.IsBoundToSession
                                    && (
                                        start.SessionDeadline is not { } deadline || deadline <= now
                                    )
                                )
                                {
                                    result = ElectionResult.SessionEnded;
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
                                            ? ElectionResult.Elected(
                                                new Leadership(
                                                    new Leader(
                                                        role,
                                                        new FencingToken(live.Fencing),
                                                        live.Value ?? []
                                                    ),
                                                    new OwnerToken(live.Owner),
                                                    terms
                                                )
                                            )
                                            : ElectionResult.Led;
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
                                            role,
                                            Slot,
                                            Capacity,
                                            fencing,
                                            owner,
                                            sessionId,
                                            grantDeadline,
                                            bytes
                                        )
                                    );
                                    start.Change.Write(Grants.Fence(id, Kind, role, fencing));

                                    result = ElectionResult.Elected(
                                        new Leadership(
                                            new Leader(role, new FencingToken(fencing), bytes),
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

        var microseconds = PgCall.ToMicroseconds(timeToLive);
        var role = leadership.Leader.Role;

        return await ChangeHeldAsync(
                leadership,
                (id, row, now) =>
                    Grants.Renew(
                        id,
                        Kind,
                        role,
                        Slot,
                        row.Fencing,
                        now.AddTicks(microseconds * 10)
                    ),
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
                async (id, token) =>
                    await Grants.ReadAsync(id, Kind, role, token).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        var row = rows.FirstOrDefault(candidate => candidate.Slot == Slot);

        return new ElectionView(
            role,
            row is null ? null : new Leader(role, new FencingToken(row.Fencing), row.Value ?? [])
        );
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
        var role = leadership.Leader.Role;

        return await Grants
            .Table.Keyspaces.RunAsync<bool>(
                role.Keyspace,
                async (id, token) =>
                    await Grants
                        .Table.Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Grants
                                    .BeginAsync(transaction, id, Kind, role, null, transactionToken)
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions(null);

                                var now = start.Change.Now;
                                var held = start.Rows.FirstOrDefault(row =>
                                    row.Slot == Slot
                                    && row.Fencing == leadership.Leader.Term.Value
                                    && string.Equals(
                                        row.Owner,
                                        leadership.Owner.Value,
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
