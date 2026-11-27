// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Transactions;

/// <summary>
/// The transactions of the PostgreSQL provider: one server transaction locks the keyspace's row, reads the rows of
/// every key the conditions and the writes name, evaluates the conditions and commits the branch's writes with the
/// change log.
/// </summary>
/// <param name="table">The record table that the record service shares.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal sealed class PgTransactionService(PgRecordTable table) : ITransactionService
{
    /// <summary>
    /// Gets the record table that the record service shares.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <inheritdoc />
    public async ValueTask<TransactionResult> ExecuteAsync(
        RecordTransaction transaction,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        PgCall.Check(options, cancellationToken);

        var keys = transaction
            .Conditions.Select(condition => condition.Key)
            .Concat(transaction.OnSuccess.Select(write => write.Key))
            .Concat(transaction.OnFailure.Select(write => write.Key))
            .Distinct()
            .ToArray();

        var sessions = transaction
            .OnSuccess.Concat(transaction.OnFailure)
            .Select(write => write.Lifetime?.Session)
            .OfType<SessionId>()
            .Where(session => session.Keyspace == transaction.Keyspace)
            .Select(session => session.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return await Table
            .Keyspaces.RunAsync<TransactionResult>(
                transaction.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (pgTransaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(pgTransaction, id, keys, sessions, transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                var ended = start.EndExpiredSessions();
                                var live = new Dictionary<CoordinationKey, PgRecordRow>();
                                var replaceable = new Dictionary<CoordinationKey, PgRecordRow>();
                                var expired = new List<PgRecordRow>();
                                foreach (var (key, row) in start.Rows)
                                {
                                    switch (row.StateAt(change.Now))
                                    {
                                        case PgRecordState.Live:
                                            live[key] = row;
                                            break;
                                        case PgRecordState.Expired:
                                            expired.Add(row);
                                            break;
                                        default:
                                            replaceable[key] = row;
                                            break;
                                    }
                                }

                                change.Expire(expired);

                                var succeeded = ConditionsHold(transaction, live);
                                var writes = succeeded
                                    ? transaction.OnSuccess
                                    : transaction.OnFailure;
                                var status = succeeded
                                    ? TransactionStatus.Succeeded
                                    : TransactionStatus.Failed;

                                if (
                                    writes.Any(write =>
                                        write.Lifetime?.Session is { } session
                                        && IsEnded(start, ended, transaction.Keyspace, session)
                                    )
                                )
                                {
                                    await Table
                                        .CommitAsync(pgTransaction, change, id, transactionToken)
                                        .ConfigureAwait(false);
                                    return new TransactionResult(
                                        TransactionStatus.SessionEnded,
                                        new Revision(change.Revision)
                                    );
                                }

                                if (!writes.IsEmpty)
                                {
                                    change.Begin();

                                    foreach (var write in writes)
                                    {
                                        if (write.Lifetime is { } lifetime)
                                        {
                                            var timeToLive = lifetime.TimeToLive;
                                            DateTime? deadline = timeToLive is null
                                                ? null
                                                : change.Now.AddTicks(
                                                    PgCall.ToMicroseconds(timeToLive) * 10
                                                );
                                            change.Put(
                                                write.Key,
                                                write.Value,
                                                deadline,
                                                lifetime.Session?.Value,
                                                replaceable.Remove(write.Key, out var replaced)
                                                    ? replaced.Session
                                                    : null
                                            );
                                            continue;
                                        }

                                        if (live.Remove(write.Key, out var row))
                                        {
                                            change.Delete(row);
                                        }
                                    }
                                }

                                await Table
                                    .CommitAsync(pgTransaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return new TransactionResult(status, new Revision(change.Revision));
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tells whether a write's session has ended, was never opened in the transaction's keyspace, or was ended by
    /// this transaction because it had expired.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="ended">The sessions that the transaction ended because they had expired.</param>
    /// <param name="keyspace">The transaction's keyspace.</param>
    /// <param name="session">The session that a write binds a record to.</param>
    /// <returns>True when the session is not live.</returns>
    private static bool IsEnded(
        PgChangeStart start,
        ImmutableHashSet<string> ended,
        Keyspace keyspace,
        SessionId session
    ) =>
        session.Keyspace != keyspace
        || ended.Contains(session.Value)
        || !start.Sessions.ContainsKey(session.Value);

    /// <summary>
    /// Checks whether every condition of a transaction holds over the live rows.
    /// </summary>
    /// <param name="transaction">The transaction whose conditions are checked.</param>
    /// <param name="live">The live rows of the keys, by key.</param>
    /// <returns>True when every condition holds, and when there are none.</returns>
    private static bool ConditionsHold(
        RecordTransaction transaction,
        Dictionary<CoordinationKey, PgRecordRow> live
    )
    {
        foreach (var condition in transaction.Conditions)
        {
            var found = live.TryGetValue(condition.Key, out var row);

            var holds = condition.Kind switch
            {
                RecordConditionKind.Absent => !found,
                RecordConditionKind.RevisionEquals => found
                    && condition.Revision is { } revision
                    && row?.Revision == revision.Value,
                RecordConditionKind.ValueEquals => found
                    && row is not null
                    && row.Value.AsSpan().SequenceEqual(condition.Value.Span),
                _ => throw new ArgumentException(
                    $"The condition kind '{condition.Kind}' is not known.",
                    nameof(transaction)
                ),
            };

            if (!holds)
            {
                return false;
            }
        }

        return true;
    }
}
