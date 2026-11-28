// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Notifications;

/// <summary>
/// The notifications of the PostgreSQL provider: a topic is one row that numbers its messages, each message is a row
/// of a log that the sweep trims, and a publish wakes the subscribers with <c>pg_notify</c>.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="settings">The settings of the provider.</param>
/// <param name="hub">The owner of the provider's listening connection and logs' fanouts.</param>
/// <exception cref="ArgumentNullException">An argument is null.</exception>
internal sealed class PgNotificationService(
    PgServer server,
    PgKeyspaces keyspaces,
    PgSettings settings,
    PgLogHub hub
) : INotificationService
{
    /// <summary>
    /// The statement that takes a topic's next sequence, writes the message and wakes the keyspace's channel. Its
    /// parameters are the keyspace id, the index, the depth, the key's text, the payload, the retention in
    /// microseconds, the channel and the start of the wake-up payload.
    /// </summary>
    private const string PublishSql = """
        WITH t AS (
          INSERT INTO {schema}.topics AS t (keyspace_id, idx, depth, key, sequence) VALUES ($1, $2, $3, $4, 1)
          ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET sequence = t.sequence + 1 RETURNING sequence),
        m AS (
          INSERT INTO {schema}.messages (keyspace_id, idx, depth, sequence, payload, deadline)
          SELECT $1, $2, $3, t.sequence, $5, clock_timestamp() + $6 * interval '1 microsecond' FROM t RETURNING sequence)
        SELECT m.sequence, pg_notify($7, $8 || m.sequence::text) FROM m
        """;

    /// <summary>
    /// The statement that reads a topic's log: its highest removed sequence and current sequence, which are 0 and 0
    /// without a topic row, and the messages after a sequence. Its parameters are the keyspace id, the index, the
    /// depth, the sequence to read after and the most messages to return.
    /// </summary>
    private const string ReadLogSql = """
        SELECT t.removed, t.sequence, m.sequence, m.payload
        FROM (SELECT 1) x
        LEFT JOIN {schema}.topics t ON t.keyspace_id = $1 AND t.idx = $2 AND t.depth = $3
        LEFT JOIN LATERAL (
          SELECT sequence, payload FROM {schema}.messages
          WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND sequence > $4
          ORDER BY sequence LIMIT $5) m ON true
        """;

    /// <summary>
    /// Reads the result of the statement that publishes a message.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The sequence of the message that was written.</returns>
    private static async ValueTask<ImmutableArray<long>> ReadPublishedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a row of the result of the statement that reads a topic's log.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row.</returns>
    private static PgLogRow ReadLogRow(NpgsqlDataReader reader)
    {
        var removed = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
        var current = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
        var message = ReadMessage(reader);

        return new PgLogRow(removed, current, message);
    }

    /// <summary>
    /// Reads the message that a row of a topic's log holds.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The message, or null when the row holds none.</returns>
    private static PgMessage? ReadMessage(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(2))
        {
            return null;
        }

        var number = reader.GetInt64(2);
        var payload = reader.GetFieldValue<byte[]>(3);

        return new PgMessage(number, payload);
    }

    /// <summary>
    /// Reads the result of the statement that reads a topic's log.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rows of the log.</returns>
    private static async ValueTask<ImmutableArray<PgLogRow>> ReadLogRowsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadLogRow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds what a read of a topic's log returns from the rows that were read.
    /// </summary>
    /// <param name="rows">The rows, which hold the same removed and current sequences.</param>
    /// <returns>The highest removed sequence, the current sequence and the messages that the rows hold.</returns>
    private static PgLogRead<PgMessage> ToLogRead(ImmutableArray<PgLogRow> rows)
    {
        if (rows.Length == 0)
        {
            return new PgLogRead<PgMessage>(0, 0, []);
        }

        var messages = ImmutableArray.CreateBuilder<PgMessage>();
        foreach (var row in rows)
        {
            if (row.Message is { } message)
            {
                messages.Add(message);
            }
        }

        var entries = messages.ToImmutable();

        return new PgLogRead<PgMessage>(rows[0].Removed, rows[0].Current, entries);
    }

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the resolver of keyspace ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; } =
        keyspaces ?? throw new ArgumentNullException(nameof(keyspaces));

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    private PgSettings Settings { get; } =
        settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>
    /// Gets the owner of the provider's listening connection and logs' fanouts.
    /// </summary>
    private PgLogHub Hub { get; } = hub ?? throw new ArgumentNullException(nameof(hub));

    /// <summary>
    /// Gets the statement that publishes a message.
    /// </summary>
    private string Publish { get; } = settings.Schema.Apply(PublishSql);

    /// <summary>
    /// Gets the statement that reads a topic's log.
    /// </summary>
    private string ReadLog { get; } = settings.Schema.Apply(ReadLogSql);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The payload is longer than the limit.</exception>
    /// <remarks>
    /// The operation is one statement that numbers the message, writes it and calls <c>pg_notify</c>.
    /// </remarks>
    public async ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (payload.Length > CoordinationLimits.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"A payload has at most {CoordinationLimits.MaxPayloadLength} bytes.",
                nameof(payload)
            );
        }

        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(topic);
        var depth = (short)topic.PartitionDepth;
        var text = PgKeys.KeyText(topic);
        var body = payload.ToArray();
        var retention = PgCall.ToMicroseconds(Settings.LogRetention);
        var announcement = "t:" + PgKeys.PageToken(index, depth) + ":";

        var request = new PgPublishRequest(index, depth, text, body, retention, announcement);

        await Keyspaces
            .RunAsync(
                topic.Keyspace,
                (id, token) => PublishInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stream starts with a resync, then yields each message in order; a resync follows each gap, a reconnect and
    /// the loss of a message that did not fit in the subscriber's queue. The checks run when the stream is first
    /// advanced.
    /// </remarks>
    public async IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        PgCall.Check(options, cancellationToken);

        var (logId, subscriber) = await SubscribeToLogAsync(topic, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            long lastDelivered = 0;
            await foreach (
                var item in subscriber.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                if (item.IsResync)
                {
                    yield return Notification.Resync(topic, lastDelivered);
                    continue;
                }

                if (item.Entry is not { } message)
                {
                    continue;
                }

                lastDelivered = message.Number;
                yield return Notification.Message(topic, message.Number, message.Payload);
            }
        }
        finally
        {
            await Hub.UnsubscribeAsync(logId, subscriber).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a topic's keyspace and subscribes to the topic's message log.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The log's id and the subscriber that reads it.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<(
        PgLogId LogId,
        PgLogSubscriber<PgMessage> Subscriber
    )> SubscribeToLogAsync(CoordinationKey topic, CancellationToken cancellationToken)
    {
        var index = PgKeys.Index(topic);
        var depth = (short)topic.PartitionDepth;

        var keyspaceId = await Keyspaces
            .ResolveAsync(topic.Keyspace, cancellationToken)
            .ConfigureAwait(false);

        var topicToken = PgKeys.PageToken(index, depth);
        var logId = new PgLogId(keyspaceId, topicToken);
        var address = new PgTopicAddress(keyspaceId, index, depth);
        var subscriber = await Hub.SubscribeAsync(
                logId,
                () => CreateFanout(address),
                null,
                cancellationToken
            )
            .ConfigureAwait(false);

        return (logId, subscriber);
    }

    /// <summary>
    /// Creates the fanout that reads a topic's message log.
    /// </summary>
    /// <param name="topic">The address of the topic's log.</param>
    /// <returns>A fanout that reads the topic's message log.</returns>
    private PgLogFanout<PgMessage> CreateFanout(PgTopicAddress topic)
    {
        return new PgLogFanout<PgMessage>(
            (after, limit, readToken) => ReadLogAsync(topic, after, limit, readToken)
        );
    }

    /// <summary>
    /// Publishes a message to a topic in a keyspace, in one statement.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the publish needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <exception cref="CoordinationException">The statement did not write exactly one message.</exception>
    private async ValueTask PublishInKeyspaceAsync(
        long id,
        PgPublishRequest request,
        CancellationToken cancellationToken
    )
    {
        var channel = PgChannels.ForKeyspace(Settings.Schema, id);
        var statement = new PgStatement(
            Publish,
            id,
            request.Index,
            request.Depth,
            request.Text,
            request.Body,
            request.Retention,
            channel,
            request.Announcement
        );

        var rows = await Server
            .ExecuteAsync([statement], ReadPublishedAsync, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Length != 1)
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unavailable,
                true,
                $"A publish returned {rows.Length} rows instead of one."
            );
        }
    }

    /// <summary>
    /// Reads a topic's message log after a sequence, in one statement.
    /// </summary>
    /// <param name="topic">The address of the topic's log.</param>
    /// <param name="after">The sequence to read after.</param>
    /// <param name="limit">The most messages to return.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The highest removed sequence, the current sequence and the messages after the given one.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<PgLogRead<PgMessage>> ReadLogAsync(
        PgTopicAddress topic,
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(
            ReadLog,
            topic.KeyspaceId,
            topic.Index,
            topic.Depth,
            after,
            (long)limit
        );

        var rows = await Server
            .ExecuteAsync([statement], ReadLogRowsAsync, cancellationToken)
            .ConfigureAwait(false);

        return ToLogRead(rows);
    }
}
