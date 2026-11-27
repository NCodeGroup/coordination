// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Sets;

/// <summary>
/// The sets of the PostgreSQL provider: each is one row of the sets table and one row of the set members table for
/// each member, whose deadline the server's clock decides.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="schema">The schema that holds the sets tables.</param>
/// <exception cref="ArgumentNullException">The server or the keyspaces are null.</exception>
internal sealed class PgSetService(PgServer server, PgKeyspaces keyspaces, PgSchemaName schema)
    : ISetService
{
    /// <summary>
    /// The statement that takes the row lock of a set, inserting the row when the sweep removed it meanwhile.
    /// </summary>
    private const string LockSql = """
        INSERT INTO {schema}.sets AS s (keyspace_id, idx, depth, key)
        VALUES ($1, $2, $3, $4)
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET key = EXCLUDED.key
        RETURNING 1
        """;

    /// <summary>
    /// The statement that reads the server's clock, the deadline of a member and the number of live members.
    /// </summary>
    private const string ReadSql = """
        SELECT now.t,
          (SELECT m.deadline FROM {schema}.set_members m
           WHERE m.keyspace_id = $1 AND m.idx = $2 AND m.depth = $3 AND m.member = $4),
          (SELECT count(*) FROM {schema}.set_members m
           WHERE m.keyspace_id = $1 AND m.idx = $2 AND m.depth = $3 AND m.deadline > now.t)
        FROM (SELECT clock_timestamp() AS t) now
        """;

    /// <summary>The statement that writes a member and its deadline.</summary>
    private const string WriteSql = """
        INSERT INTO {schema}.set_members (keyspace_id, idx, depth, member, deadline)
        VALUES ($1, $2, $3, $4, $5)
        ON CONFLICT (keyspace_id, idx, depth, member) DO UPDATE SET deadline = EXCLUDED.deadline
        """;

    /// <summary>The statement that deletes a member and tells whether an unexpired one was deleted.</summary>
    private const string RemoveSql = """
        WITH now AS (SELECT clock_timestamp() AS t),
        d AS (
          DELETE FROM {schema}.set_members
          WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND member = $4
          RETURNING deadline
        )
        SELECT EXISTS (SELECT 1 FROM d, now WHERE d.deadline > now.t)
        """;

    /// <summary>The statement that lists the unexpired members of a set.</summary>
    private const string ListSql = """
        SELECT member FROM {schema}.set_members
        WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND deadline > clock_timestamp()
        ORDER BY member
        """;

    /// <summary>The statement that deletes the row of a set.</summary>
    private const string ClearSetSql = """
        DELETE FROM {schema}.sets WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
        """;

    /// <summary>The statement that deletes every member of a set.</summary>
    private const string ClearMembersSql = """
        DELETE FROM {schema}.set_members WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
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
    /// Gets the statement that takes the row lock of a set.
    /// </summary>
    private string Lock { get; } = schema.Apply(LockSql);

    /// <summary>
    /// Gets the statement that reads the server's clock, the deadline of a member and the number of live members.
    /// </summary>
    private string Read { get; } = schema.Apply(ReadSql);

    /// <summary>
    /// Gets the statement that writes a member and its deadline.
    /// </summary>
    private string Write { get; } = schema.Apply(WriteSql);

    /// <summary>
    /// Gets the statement that deletes a member and tells whether an unexpired one was deleted.
    /// </summary>
    private string Remove { get; } = schema.Apply(RemoveSql);

    /// <summary>
    /// Gets the statement that lists the unexpired members of a set.
    /// </summary>
    private string List { get; } = schema.Apply(ListSql);

    /// <summary>
    /// Gets the statement that deletes the row of a set.
    /// </summary>
    private string ClearSet { get; } = schema.Apply(ClearSetSql);

    /// <summary>
    /// Gets the statement that deletes every member of a set.
    /// </summary>
    private string ClearMembers { get; } = schema.Apply(ClearMembersSql);

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one transaction. Its first batch takes the row lock of the set and reads the server's clock,
    /// the member's deadline and the number of live members, the decision is made here, and its second batch writes
    /// the member and commits. A member that is absent from a full set rolls back and fails as
    /// <c>limit-exceeded</c>, not applied.
    /// </remarks>
    public async ValueTask<bool> AddAsync(
        CoordinationKey key,
        KeySegment member,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(timeToLive);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);
        var encoded = PgKeys.EncodeSegment(member);
        var microseconds = PgCall.ToMicroseconds(timeToLive);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, runToken) =>
                    await Server
                        .TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, token) =>
                            {
                                var (now, deadline, live) = await transaction
                                    .FirstAsync(
                                        [
                                            new PgStatement(
                                                Lock,
                                                PgArgument.Of(id),
                                                PgArgument.Of(index),
                                                PgArgument.Of(depth),
                                                PgArgument.Of(text)
                                            ),
                                            new PgStatement(
                                                Read,
                                                PgArgument.Of(id),
                                                PgArgument.Of(index),
                                                PgArgument.Of(depth),
                                                PgArgument.Of(encoded)
                                            ),
                                        ],
                                        async (results, t) =>
                                        {
                                            await results
                                                .ReadAsync(reader => reader.GetInt32(0), t)
                                                .ConfigureAwait(false);
                                            var rows = await results
                                                .ReadAsync(
                                                    reader =>
                                                        (
                                                            Now: reader.GetDateTime(0),
                                                            Deadline: reader.IsDBNull(1)
                                                                ? (DateTime?)null
                                                                : reader.GetDateTime(1),
                                                            Live: reader.GetInt64(2)
                                                        ),
                                                    t
                                                )
                                                .ConfigureAwait(false);

                                            return rows[0];
                                        },
                                        token
                                    )
                                    .ConfigureAwait(false);

                                var instant = DateTime.SpecifyKind(now, DateTimeKind.Utc);
                                var wasAbsent =
                                    deadline is not { } stored
                                    || DateTime.SpecifyKind(stored, DateTimeKind.Utc) <= instant;

                                if (wasAbsent && live >= CoordinationLimits.MaxSetMembers)
                                {
                                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                                    throw new CoordinationException(
                                        CoordinationErrorCodes.LimitExceeded,
                                        outcomeUnknown: false,
                                        $"A set holds at most {CoordinationLimits.MaxSetMembers} live members."
                                    );
                                }

                                var expires = instant.AddTicks(microseconds * 10);

                                await transaction
                                    .CommitAsync(
                                        [
                                            new PgStatement(
                                                Write,
                                                PgArgument.Of(id),
                                                PgArgument.Of(index),
                                                PgArgument.Of(depth),
                                                PgArgument.Of(encoded),
                                                PgArgument.Typed(expires, NpgsqlDbType.TimestampTz)
                                            ),
                                        ],
                                        token
                                    )
                                    .ConfigureAwait(false);

                                return wasAbsent;
                            },
                            runToken
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one statement that deletes the row and tells whether the row was unexpired.
    /// </remarks>
    public async ValueTask<bool> RemoveAsync(
        CoordinationKey key,
        KeySegment member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var encoded = PgKeys.EncodeSegment(member);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    Remove,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth),
                                    PgArgument.Of(encoded)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetBoolean(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return rows[0];
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one select of the unexpired members. A member that doesn't decode fails as
    /// <c>unavailable</c>, not applied.
    /// </remarks>
    public async ValueTask<ImmutableArray<KeySegment>> ListAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    List,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetString(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    var members = ImmutableArray.CreateBuilder<KeySegment>(rows.Length);
                    foreach (var row in rows)
                    {
                        if (!PgKeys.TryDecodeSegment(row, out var member))
                        {
                            throw new CoordinationException(
                                CoordinationErrorCodes.Unavailable,
                                outcomeUnknown: false,
                                "The server returned a set member the provider didn't write."
                            );
                        }

                        members.Add(member);
                    }

                    return members.MoveToImmutable();
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one batch that deletes the set's row and then its members, so that a clear waits for an add
    /// that holds the set's row.
    /// </remarks>
    public async ValueTask ClearAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                    await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    ClearSet,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth)
                                ),
                                new PgStatement(
                                    ClearMembers,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth)
                                ),
                            ],
                            (_, _) => ValueTask.FromResult(true),
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
