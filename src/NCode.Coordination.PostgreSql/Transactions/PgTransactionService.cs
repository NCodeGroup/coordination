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
        CheckNoSession(transaction.OnSuccess);
        CheckNoSession(transaction.OnFailure);

        var keys = transaction
            .Conditions.Select(condition => condition.Key)
            .Concat(transaction.OnSuccess.Select(write => write.Key))
            .Concat(transaction.OnFailure.Select(write => write.Key))
            .Distinct()
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
                                    .BeginAsync(pgTransaction, id, keys, [], transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                var live = new Dictionary<CoordinationKey, PgRecordRow>();
                                var expired = new List<PgRecordRow>();
                                foreach (var (key, row) in start.Rows)
                                {
                                    if (row.StateAt(change.Now) == PgRecordState.Live)
                                    {
                                        live[key] = row;
                                    }
                                    else
                                    {
                                        expired.Add(row);
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
                                                null,
                                                null
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
    /// Refuses a branch whose writes bind a record to a session, which this build keeps none of.
    /// </summary>
    /// <param name="writes">The writes of a branch.</param>
    /// <exception cref="CoordinationException">A write is bound to a session; the call did not apply.</exception>
    private static void CheckNoSession(ImmutableArray<RecordWrite> writes)
    {
        foreach (var write in writes)
        {
            if (write.Lifetime?.Session is not null)
            {
                throw new CoordinationException(
                    CoordinationErrorCodes.Unsupported,
                    outcomeUnknown: false,
                    "The PostgreSQL provider keeps no sessions in this build."
                );
            }
        }
    }

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
