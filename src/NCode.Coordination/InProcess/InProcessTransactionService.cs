// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The transactions of the in-process provider.
/// </summary>
/// <param name="table">The record table, shared with the record service.</param>
/// <remarks>
/// A transaction evaluates its conditions and applies its writes while holding the table's lock, so it is atomic across
/// partitions and linearizable in the process.
/// </remarks>
internal class InProcessTransactionService(InProcessRecordTable table) : ITransactionService
{
    /// <summary>
    /// Gets the record table, shared with the record service.
    /// </summary>
    private InProcessRecordTable Table { get; } = table;

    /// <inheritdoc />
    public ValueTask<TransactionResult> ExecuteAsync(
        RecordTransaction transaction,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        InProcessGuard.Check(options, cancellationToken);

        lock (Table.Gate)
        {
            var succeeded = ConditionsHold(transaction);
            var writes = succeeded ? transaction.OnSuccess : transaction.OnFailure;

            foreach (var write in writes)
            {
                if (write.Lifetime?.Session is not null)
                {
                    throw new CoordinationException(
                        CoordinationErrorCodes.Unsupported,
                        outcomeUnknown: false,
                        "The in-process provider keeps no sessions, so it binds no record to one."
                    );
                }
            }

            if (writes.IsEmpty)
            {
                var current = Table.Current(transaction.Keyspace);
                var unchanged = new TransactionResult(succeeded, current);
                return ValueTask.FromResult(unchanged);
            }

            var revision = Table.Advance(transaction.Keyspace);

            foreach (var write in writes)
            {
                if (write.Lifetime is { } lifetime)
                {
                    var record = new StoredRecord(write.Key, write.Value, revision);
                    var expiry = InProcessExpiry.ForLifetime(Table.Time, lifetime);
                    Table.Put(record, expiry);
                }
                else
                {
                    Table.Remove(write.Key);
                }
            }

            var result = new TransactionResult(succeeded, revision);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// Checks whether every condition of a transaction holds. The caller holds the table's lock.
    /// </summary>
    /// <param name="transaction">The transaction whose conditions are checked.</param>
    /// <returns>True when every condition holds, and when there are none.</returns>
    private bool ConditionsHold(RecordTransaction transaction)
    {
        foreach (var condition in transaction.Conditions)
        {
            var record = Table.Find(condition.Key);

            var holds = condition.Kind switch
            {
                RecordConditionKind.Absent => record is null,
                RecordConditionKind.RevisionEquals => record is not null
                    && record.Revision == condition.Revision,
                RecordConditionKind.ValueEquals => record is not null
                    && record.Value.Span.SequenceEqual(condition.Value.Span),
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
