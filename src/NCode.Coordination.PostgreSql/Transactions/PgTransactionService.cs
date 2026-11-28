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
    /// Tells whether a write's session has ended, was never opened in the transaction's keyspace, or was ended by
    /// this transaction because it had expired.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="keyspace">The transaction's keyspace.</param>
    /// <param name="session">The session that a write binds a record to.</param>
    /// <returns>True when the session is not live.</returns>
    private static bool IsEnded(PgChangeStart start, Keyspace keyspace, SessionId session) =>
        session.Keyspace != keyspace || !start.IsLiveSession(session.Value);

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

    /// <summary>
    /// Gets the distinct keys that the conditions and the writes of both branches name.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The keys.</returns>
    private static CoordinationKey[] KeysOf(RecordTransaction transaction)
    {
        var conditionKeys = transaction.Conditions.Select(condition => condition.Key);
        var successKeys = transaction.OnSuccess.Select(write => write.Key);
        var failureKeys = transaction.OnFailure.Select(write => write.Key);

        return conditionKeys.Concat(successKeys).Concat(failureKeys).Distinct().ToArray();
    }

    /// <summary>
    /// Gets the ids of the distinct sessions of the transaction's keyspace that the writes of both branches bind
    /// records to.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The sessions' ids.</returns>
    private static string[] SessionsOf(RecordTransaction transaction)
    {
        return transaction
            .OnSuccess.Concat(transaction.OnFailure)
            .Select(write => write.Lifetime?.Session)
            .OfType<SessionId>()
            .Where(session => session.Keyspace == transaction.Keyspace)
            .Select(session => session.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Sorts the rows that the first batch read by what the transaction may do with each.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <returns>The live rows, the rows that a write may replace and the rows that have expired.</returns>
    private static PgTransactionRows ClassifyRows(PgChangeStart start)
    {
        var live = new Dictionary<CoordinationKey, PgRecordRow>(start.Rows.Count);
        var replaceable = new Dictionary<CoordinationKey, PgRecordRow>(start.Rows.Count);
        var expired = new List<PgRecordRow>(start.Rows.Count);

        foreach (var (key, row) in start.Rows)
        {
            switch (row.StateAt(start.Change.Now))
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

        return new PgTransactionRows(live, replaceable, expired);
    }

    /// <summary>
    /// Tells whether a branch's writes bind a record to a session that is not live.
    /// </summary>
    /// <param name="writes">The writes of the branch.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="keyspace">The transaction's keyspace.</param>
    /// <returns>True when a write binds to a session that is not live.</returns>
    private static bool WritesBindToEndedSession(
        ImmutableArray<RecordWrite> writes,
        PgChangeStart start,
        Keyspace keyspace
    )
    {
        return writes.Any(write =>
            write.Lifetime?.Session is { } session && IsEnded(start, keyspace, session)
        );
    }

    /// <summary>
    /// Gets the instant a record written now expires.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="lifetime">The record's lifetime.</param>
    /// <returns>The deadline, or null when the lifetime has no time to live.</returns>
    private static DateTime? DeadlineOf(PgKeyspaceChange change, RecordLifetime lifetime)
    {
        if (lifetime.TimeToLive is not { } timeToLive)
        {
            return null;
        }

        return PgCall.DeadlineAfter(change.Now, timeToLive);
    }

    /// <summary>
    /// Gets the session that the row a write replaces was bound to.
    /// </summary>
    /// <param name="replaceable">The rows that a write may replace, by key.</param>
    /// <param name="key">The key that the write names.</param>
    /// <returns>The id of the ended session whose row the write replaces, or null.</returns>
    private static string? ReplacedSessionOf(
        Dictionary<CoordinationKey, PgRecordRow> replaceable,
        CoordinationKey key
    )
    {
        return replaceable.TryGetValue(key, out var replaced) ? replaced.Session : null;
    }

    /// <summary>
    /// Writes a record that a write puts, in the current entry of the change.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="write">The write.</param>
    /// <param name="lifetime">The write's lifetime.</param>
    /// <param name="replaceable">The rows that a write may replace, by key.</param>
    private static void PutRecord(
        PgKeyspaceChange change,
        RecordWrite write,
        RecordLifetime lifetime,
        Dictionary<CoordinationKey, PgRecordRow> replaceable
    )
    {
        var deadline = DeadlineOf(change, lifetime);
        var session = lifetime.Session?.Value;
        var replacedSession = ReplacedSessionOf(replaceable, write.Key);

        var put = new PgRecordPut(write.Key, write.Value, deadline, session)
        {
            ReplacedSession = replacedSession,
        };
        change.Put(put);
    }

    /// <summary>
    /// Takes a revision and makes a branch's writes in the change.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="writes">The writes of the branch, which has at least one.</param>
    /// <param name="rows">The rows that the first batch read.</param>
    private static void ApplyWrites(
        PgKeyspaceChange change,
        ImmutableArray<RecordWrite> writes,
        PgTransactionRows rows
    )
    {
        change.Begin();

        foreach (var write in writes)
        {
            if (write.Lifetime is { } lifetime)
            {
                PutRecord(change, write, lifetime, rows.Replaceable);
                rows.Replaceable.Remove(write.Key);
            }
            else if (rows.Live.Remove(write.Key, out var row))
            {
                change.Delete(row);
            }
        }
    }

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

        var keys = KeysOf(transaction);
        var sessions = SessionsOf(transaction);
        var request = new PgTransactionRequest(transaction, keys, sessions);

        return await Table
            .RunInKeyspaceAsync<TransactionResult>(
                transaction.Keyspace,
                (id, token) => ExecuteInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a transaction in a keyspace, in a server transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">The transaction and what its first batch asks.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the transaction.</returns>
    private async ValueTask<TransactionResult> ExecuteInKeyspaceAsync(
        long id,
        PgTransactionRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (pgTransaction, token) =>
                    ExecuteInTransactionAsync(pgTransaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, evaluates the conditions over the rows that were read, makes the branch's writes and commits
    /// the change.
    /// </summary>
    /// <param name="pgTransaction">The server transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">The transaction and what its first batch asks.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the transaction.</returns>
    private async ValueTask<TransactionResult> ExecuteInTransactionAsync(
        PgTransaction pgTransaction,
        long id,
        PgTransactionRequest request,
        CancellationToken cancellationToken
    )
    {
        var transaction = request.Transaction;
        var scope = new PgRecordScope(id, request.Keys, request.Sessions);
        var start = await Table
            .BeginAsync(pgTransaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        var rows = ClassifyRows(start);
        change.Expire(rows.Expired);

        var succeeded = ConditionsHold(transaction, rows.Live);
        var writes = succeeded ? transaction.OnSuccess : transaction.OnFailure;
        var status = succeeded ? TransactionStatus.Succeeded : TransactionStatus.Failed;

        if (WritesBindToEndedSession(writes, start, transaction.Keyspace))
        {
            status = TransactionStatus.SessionEnded;
        }
        else if (!writes.IsEmpty)
        {
            ApplyWrites(change, writes, rows);
        }

        await Table.CommitAsync(pgTransaction, change, id, cancellationToken).ConfigureAwait(false);

        var revision = new Revision(change.Revision);
        return new TransactionResult(status, revision);
    }
}
