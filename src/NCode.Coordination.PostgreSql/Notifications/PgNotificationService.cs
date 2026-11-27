// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;

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

        await Keyspaces
            .RunAsync(
                topic.Keyspace,
                async (id, runToken) =>
                {
                    var channel = PgChannels.ForKeyspace(Settings.Schema, id);

                    var rows = await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    Publish,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth),
                                    PgArgument.Of(text),
                                    PgArgument.Of(body),
                                    PgArgument.Of(retention),
                                    PgArgument.Of(channel),
                                    PgArgument.Of(announcement)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false),
                            runToken
                        )
                        .ConfigureAwait(false);

                    if (rows.Length != 1)
                    {
                        throw new CoordinationException(
                            CoordinationErrorCodes.Unavailable,
                            true,
                            $"A publish returned {rows.Length} rows instead of one."
                        );
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        _ = Hub;
        throw new NotImplementedException("Subscriptions arrive in a later step.");
    }
}
