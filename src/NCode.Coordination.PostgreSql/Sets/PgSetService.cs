// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

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
        INSERT INTO {schema}.sets AS s (keyspace_id, idx, depth, key, deadline)
        VALUES ($1, $2, $3, $4, clock_timestamp())
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET key = EXCLUDED.key
        RETURNING 1
        """;

    /// <summary>
    /// The statement that reads the server's clock, the member and the set's bound, counts the live members only when
    /// the bound has reached the limit, writes the member when it is present or the set has room, keeps the bound, and
    /// returns whether the member was present and whether it was written.
    /// </summary>
    private const string AddSql = """
        WITH now AS (SELECT clock_timestamp() AS t),
        cur AS (
          SELECT now.t,
            m.deadline IS NOT NULL AS stored,
            coalesce(m.deadline > now.t, false) AS present,
            s.members AS bound
          FROM now
          CROSS JOIN {schema}.sets s
          LEFT JOIN {schema}.set_members m
            ON m.keyspace_id = s.keyspace_id AND m.idx = s.idx AND m.depth = s.depth AND m.member = $4
          WHERE s.keyspace_id = $1 AND s.idx = $2 AND s.depth = $3
        ),
        chk AS (
          SELECT cur.t, cur.stored, cur.present, cur.bound,
            CASE WHEN cur.present OR cur.bound < $6 THEN NULL ELSE
              (SELECT count(*) FROM {schema}.set_members m
               WHERE m.keyspace_id = $1 AND m.idx = $2 AND m.depth = $3 AND m.deadline > cur.t) END AS live,
            CASE WHEN cur.present OR cur.bound < $6 THEN NULL ELSE
              (SELECT count(*) FROM {schema}.set_members m
               WHERE m.keyspace_id = $1 AND m.idx = $2 AND m.depth = $3) END AS total
          FROM cur
        ),
        w AS (
          INSERT INTO {schema}.set_members (keyspace_id, idx, depth, member, deadline)
          SELECT $1, $2, $3, $4, chk.t + $5 * interval '1 microsecond' FROM chk
          WHERE chk.present OR chk.bound < $6 OR chk.live < $6
          ON CONFLICT (keyspace_id, idx, depth, member) DO UPDATE SET deadline = EXCLUDED.deadline
          RETURNING 1
        ),
        u AS (
          UPDATE {schema}.sets s
          SET members = coalesce(chk.total, s.members) + CASE WHEN chk.stored THEN 0 ELSE 1 END,
            deadline = greatest(s.deadline, chk.t + $5 * interval '1 microsecond')
          FROM chk
          WHERE s.keyspace_id = $1 AND s.idx = $2 AND s.depth = $3 AND EXISTS (SELECT 1 FROM w)
          RETURNING 1
        )
        SELECT chk.present, EXISTS (SELECT 1 FROM w) FROM chk
        """;

    /// <summary>
    /// The statement that takes the row lock of a set when the set has a row.
    /// </summary>
    private const string LockExistingSql = """
        SELECT 1 FROM {schema}.sets WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 FOR UPDATE
        """;

    /// <summary>
    /// The statement that deletes a member, lowers the set's bound when a row was deleted, and tells whether an
    /// unexpired member was deleted.
    /// </summary>
    private const string RemoveSql = """
        WITH now AS (SELECT clock_timestamp() AS t),
        d AS (
          DELETE FROM {schema}.set_members
          WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND member = $4
          RETURNING deadline
        ),
        u AS (
          UPDATE {schema}.sets SET members = greatest(members - (SELECT count(*) FROM d), 0)
          WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND EXISTS (SELECT 1 FROM d)
          RETURNING 1
        )
        SELECT EXISTS (SELECT 1 FROM d, now WHERE d.deadline > now.t)
        """;

    /// <summary>
    /// The statement that lists the unexpired members of a set.
    /// </summary>
    private const string ListSql = """
        SELECT member FROM {schema}.set_members
        WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND deadline > clock_timestamp()
        ORDER BY member
        """;

    /// <summary>
    /// The statement that deletes the row of a set.
    /// </summary>
    private const string ClearSetSql = """
        DELETE FROM {schema}.sets WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
        """;

    /// <summary>
    /// The statement that deletes every member of a set.
    /// </summary>
    private const string ClearMembersSql = """
        DELETE FROM {schema}.set_members WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
        """;

    /// <summary>
    /// Reads the results of the add batch: the row lock, then whether the member was present and whether it was
    /// written.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>Whether the member was present before the add, and whether the add wrote it.</returns>
    private static async ValueTask<(bool Present, bool Written)> ReadAddedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        await results.SkipAsync(cancellationToken).ConfigureAwait(false);
        var rows = await results.ReadAsync(ReadAdd, cancellationToken).ConfigureAwait(false);

        return rows[0];
    }

    /// <summary>
    /// Reads the results of the remove batch: the row lock, then whether an unexpired member was deleted.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>True when an unexpired member was deleted; otherwise, false.</returns>
    private static async ValueTask<bool> ReadRemovedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        await results.SkipAsync(cancellationToken).ConfigureAwait(false);
        var rows = await results.ReadAsync(ReadRemove, cancellationToken).ConfigureAwait(false);

        return rows[0];
    }

    /// <summary>
    /// Reads the encoded members that the list statement returned.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The encoded members, in the order the server sent them.</returns>
    private static async ValueTask<ImmutableArray<string>> ReadMembersAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadMember, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the row that the add statement returned.
    /// </summary>
    /// <param name="reader">The reader, at a row.</param>
    /// <returns>Whether the member was present before the add, and whether the add wrote it.</returns>
    private static (bool Present, bool Written) ReadAdd(NpgsqlDataReader reader)
    {
        var present = reader.GetBoolean(0);
        var written = reader.GetBoolean(1);
        return (present, written);
    }

    /// <summary>
    /// Reads the row that the remove statement returned.
    /// </summary>
    /// <param name="reader">The reader, at a row.</param>
    /// <returns>True when an unexpired member was deleted; otherwise, false.</returns>
    private static bool ReadRemove(NpgsqlDataReader reader)
    {
        return reader.GetBoolean(0);
    }

    /// <summary>
    /// Reads a row that the list statement returned.
    /// </summary>
    /// <param name="reader">The reader, at a row.</param>
    /// <returns>The encoded member.</returns>
    private static string ReadMember(NpgsqlDataReader reader)
    {
        return reader.GetString(0);
    }

    /// <summary>
    /// Decodes the members that the server returned.
    /// </summary>
    /// <param name="rows">The encoded members.</param>
    /// <returns>The members' segments.</returns>
    /// <exception cref="CoordinationException">A member doesn't decode.</exception>
    private static ImmutableArray<KeySegment> DecodeMembers(ImmutableArray<string> rows)
    {
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
    /// Gets the statement that takes the row lock of a set.
    /// </summary>
    private string Lock { get; } = schema.Apply(LockSql);

    /// <summary>
    /// Gets the statement that adds a member when it is present or the set has room, and keeps the set's bound.
    /// </summary>
    private string Add { get; } = schema.Apply(AddSql);

    /// <summary>
    /// Gets the statement that takes the row lock of a set when the set has a row.
    /// </summary>
    private string LockExisting { get; } = schema.Apply(LockExistingSql);

    /// <summary>
    /// Gets the statement that deletes a member, lowers the set's bound, and tells whether an unexpired one was deleted.
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
    /// The operation is one batch, which the server runs as one transaction: it takes the row lock of the set, then
    /// one statement reads the member and the set's bound, writes the member when it is present or the set has room,
    /// and keeps the bound. The bound is never below the number of the set's member rows, so the live members are
    /// counted only when it has reached the limit, and the count then resets it. A member that is absent from a full
    /// set isn't written and fails as <c>limit-exceeded</c>, not applied.
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

        var microseconds = PgCall.ToMicroseconds(timeToLive);

        var (present, written) = await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => AddInKeyspaceAsync(id, key, member, microseconds, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!written)
        {
            throw new CoordinationException(
                CoordinationErrorCodes.LimitExceeded,
                outcomeUnknown: false,
                $"A set holds at most {CoordinationLimits.MaxSetMembers} live members."
            );
        }

        return !present;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one batch, which the server runs as one transaction: it takes the row lock of the set when the
    /// set has a row, so it waits for an add that holds it and the two never interleave, then deletes the member,
    /// lowers the set's bound when a row was deleted, and tells whether the member was unexpired.
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

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => RemoveInKeyspaceAsync(id, key, member, token),
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

        var rows = await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => ListInKeyspaceAsync(id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        return DecodeMembers(rows);
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

        await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => ClearInKeyspaceAsync(id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a member to a set in a keyspace in one batch.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the set.</param>
    /// <param name="member">The member.</param>
    /// <param name="microseconds">The time to live, in whole microseconds.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Whether the member was present before the add, and whether the add wrote it.</returns>
    private async ValueTask<(bool Present, bool Written)> AddInKeyspaceAsync(
        long id,
        CoordinationKey key,
        KeySegment member,
        long microseconds,
        CancellationToken cancellationToken
    )
    {
        var (index, text) = PgKeys.IndexAndText(key);
        var depth = (short)key.PartitionDepth;
        var encoded = PgKeys.EncodeSegment(member);
        const long maxMembers = CoordinationLimits.MaxSetMembers;

        var lockStatement = new PgStatement(Lock, id, index, depth, text);
        var addStatement = new PgStatement(
            Add,
            id,
            index,
            depth,
            encoded,
            microseconds,
            maxMembers
        );
        return await Server
            .ExecuteAsync([lockStatement, addStatement], ReadAddedAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a member from a set in a keyspace in one batch.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the set.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>True when an unexpired member was removed; otherwise, false.</returns>
    private async ValueTask<bool> RemoveInKeyspaceAsync(
        long id,
        CoordinationKey key,
        KeySegment member,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var encoded = PgKeys.EncodeSegment(member);

        var lockStatement = new PgStatement(LockExisting, id, index, depth);
        var removeStatement = new PgStatement(Remove, id, index, depth, encoded);
        return await Server
            .ExecuteAsync([lockStatement, removeStatement], ReadRemovedAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the unexpired members of a set in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the set.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The encoded members.</returns>
    private async ValueTask<ImmutableArray<string>> ListInKeyspaceAsync(
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        var statement = new PgStatement(List, id, index, depth);
        return await Server
            .ExecuteAsync([statement], ReadMembersAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a set's row and its members in a keyspace in one batch.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the set.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the batch has been read.</returns>
    private async ValueTask ClearInKeyspaceAsync(
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        var clearSetStatement = new PgStatement(ClearSet, id, index, depth);
        var clearMembersStatement = new PgStatement(ClearMembers, id, index, depth);
        await Server
            .ExecuteAsync(
                [clearSetStatement, clearMembersStatement],
                PgResults.IgnoreAsync,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
