// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The records of the PostgreSQL provider: each write is a short transaction that locks the keyspace's row, decides
/// from the rows it read and commits the writes with the change log.
/// </summary>
/// <param name="table">The record table that the operations share.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal sealed class PgRecordService(PgRecordTable table) : IRecordService
{
    /// <summary>
    /// Gets the record table that the operations share.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

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

    /// <summary>
    /// Refuses a lifetime that is bound to a session, which this build keeps none of.
    /// </summary>
    /// <param name="lifetime">The lifetime.</param>
    /// <exception cref="CoordinationException">The lifetime is bound to a session; the call did not apply.</exception>
    private static void CheckNoSession(RecordLifetime lifetime)
    {
        if (lifetime.Session is not null)
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                outcomeUnknown: false,
                "The PostgreSQL provider keeps no sessions in this build."
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);
        CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);
        CheckNoSession(lifetime);

        var copy = value.ToArray();
        var timeToLive = lifetime.TimeToLive;

        return await Table
            .Keyspaces.RunAsync<RecordWriteResult>(
                key.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    if (row.StateAt(change.Now) == PgRecordState.Live)
                                    {
                                        await Table
                                            .CommitAsync(transaction, change, id, transactionToken)
                                            .ConfigureAwait(false);
                                        return RecordWriteResult.Conflict;
                                    }

                                    change.Expire([row]);
                                }

                                var revision = change.Begin();
                                DateTime? deadline = timeToLive is null
                                    ? null
                                    : change.Now.AddTicks(PgCall.ToMicroseconds(timeToLive) * 10);
                                change.Put(key, copy, deadline, null, null);

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return RecordWriteResult.Written(new Revision(revision));
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await Table.ReadOneAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await Table
            .Keyspaces.RunAsync<StoredRecord?>(
                key.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                StoredRecord? taken = null;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    if (row.StateAt(change.Now) == PgRecordState.Live)
                                    {
                                        change.Begin();
                                        change.Delete(row);
                                        taken = new StoredRecord(
                                            key,
                                            row.Value,
                                            new Revision(row.Revision)
                                        );
                                    }
                                    else
                                    {
                                        change.Expire([row]);
                                    }
                                }

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return taken;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }
}
