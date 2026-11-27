// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;

namespace NCode.Coordination.Resp.Records;

/// <summary>
/// The record watches of the RESP provider: each subscribes to its keyspace's watch channel, whose events carry the
/// keyspace's revision and the records each change wrote or deleted, and reads a written record's value from the
/// table. It keeps no history, so every watch starts with a resync.
/// </summary>
/// <param name="table">The record table that reads the keyspace's revision.</param>
/// <param name="records">The record service that reads a written record.</param>
/// <param name="subscriptions">Opens the provider's subscriptions to numbered channels.</param>
/// <exception cref="ArgumentNullException">The table, the records or the subscriptions are null.</exception>
internal class RespRecordWatchService(
    RespRecordTable table,
    IRecordService records,
    RespSubscriptions subscriptions
) : IRecordWatchService
{
    /// <summary>
    /// Decodes the name of a record on a watch line to its key.
    /// </summary>
    /// <param name="keyspace">The keyspace of the record.</param>
    /// <param name="name">The name on the line.</param>
    /// <returns>The key, or null when the name is not one the provider wrote.</returns>
    private static CoordinationKey? TryDecode(Keyspace keyspace, string name)
    {
        try
        {
            return RespKeys.DecodeName(keyspace, name);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the record table that reads the keyspace's revision.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the record service that reads a written record.
    /// </summary>
    private IRecordService Records { get; } =
        records ?? throw new ArgumentNullException(nameof(records));

    /// <summary>
    /// Gets the object that opens the provider's subscriptions to numbered channels.
    /// </summary>
    private RespSubscriptions Subscriptions { get; } =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        return WatchCoreAsync(key.Keyspace, key.Equals, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        RespCall.Check(options, cancellationToken);

        return WatchCoreAsync(prefix.Keyspace, prefix.IsPrefixOf, cancellationToken);
    }

    /// <summary>
    /// Reads a record, reporting a failure instead of throwing it so that a stream can survive it.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>Whether the read succeeded, and the record, which is null when it is absent or the read failed.</returns>
    private async ValueTask<(bool Read, StoredRecord? Record)> TryReadAsync(
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var record = await Records
                .ReadAsync(key, null, cancellationToken)
                .ConfigureAwait(false);

            return (true, record);
        }
        catch (CoordinationException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Streams the changes of a watch: a resync at the start and after each gap, then each covered change in order.
    /// </summary>
    /// <param name="keyspace">The keyspace to watch.</param>
    /// <param name="matches">Tells whether the watch covers a record's key.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of changes.</returns>
    private async IAsyncEnumerable<RecordChange> WatchCoreAsync(
        Keyspace keyspace,
        Func<CoordinationKey, bool> matches,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var channel = RespRecordTable.WatchChannel(keyspace);
        var reader = await Subscriptions
            .OpenAsync(
                channel,
                token => Table.ReadWatchNumberAsync(keyspace, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        await using var disposal = reader.ConfigureAwait(false);

        await foreach (var item in reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var revision = new Revision(item.Number);
            if (item.IsResync)
            {
                yield return RecordChange.Resync(revision);
                continue;
            }

            if (!RespWatchLine.TryParse(item.Message, out var lines))
            {
                reader.MarkInDoubt();
                continue;
            }

            foreach (var line in lines)
            {
                if (line.Kind is not ('w' or 'd'))
                {
                    continue;
                }

                var key = TryDecode(keyspace, line.Name);
                if (key is null)
                {
                    reader.MarkInDoubt();
                    continue;
                }

                if (!matches(key))
                {
                    continue;
                }

                if (line.Kind == 'd')
                {
                    yield return RecordChange.Deleted(key, revision);
                    continue;
                }

                var (read, record) = await TryReadAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                if (!read)
                {
                    reader.MarkInDoubt();
                }
                else if (record is not null && record.Revision == revision)
                {
                    yield return RecordChange.Written(record);
                }
            }
        }
    }
}
