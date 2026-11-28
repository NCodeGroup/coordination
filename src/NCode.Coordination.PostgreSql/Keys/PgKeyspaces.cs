// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Keys;

/// <summary>
/// Resolves the id of each keyspace's row and caches it, and runs an operation again when its keyspace's row was
/// found missing.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="schema">The schema that holds the keyspaces' table.</param>
/// <param name="tokens">The source of the keyspace ids.</param>
/// <exception cref="ArgumentNullException">The server or the tokens are null.</exception>
internal sealed class PgKeyspaces(PgServer server, PgSchemaName schema, IPgTokens tokens)
{
    /// <summary>
    /// The most times that <see cref="ResolveAsync"/> draws an id before it gives up.
    /// </summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// The statement that inserts a keyspace's row unless its id or its name is taken.
    /// </summary>
    private const string InsertSql = """
        INSERT INTO {schema}.keyspaces (id, name) VALUES ($1, $2) ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The statement that reads the id of a keyspace's row.
    /// </summary>
    private const string SelectSql = """
        SELECT id FROM {schema}.keyspaces WHERE name = $1
        """;

    /// <summary>
    /// Reads the id that the batch of <see cref="InsertSql"/> and <see cref="SelectSql"/> selected.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The selected ids: one, or none when the insert's row was taken by another id.</returns>
    private static async ValueTask<ImmutableArray<long>> ReadIdsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an operation and ignores that it returns nothing, so the operation can run where a result is wanted.
    /// </summary>
    /// <param name="operation">The operation, which takes the keyspace's id.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>True, once the operation has completed.</returns>
    private static async ValueTask<bool> CompleteAsync(
        Func<long, CancellationToken, ValueTask> operation,
        long id,
        CancellationToken cancellationToken
    )
    {
        await operation(id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the source of the keyspace ids.
    /// </summary>
    private IPgTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <summary>
    /// Gets the cached id of each keyspace's row.
    /// </summary>
    private ConcurrentDictionary<Keyspace, long> Ids { get; } = new();

    /// <summary>
    /// Gets the statement that inserts a keyspace's row unless its id or its name is taken.
    /// </summary>
    private string Insert { get; } = schema.Apply(InsertSql);

    /// <summary>
    /// Gets the statement that reads the id of a keyspace's row.
    /// </summary>
    private string Select { get; } = schema.Apply(SelectSql);

    /// <summary>
    /// Gets the id of a keyspace's row, creating the row when it does not exist.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The cached id, else the id read from the server.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    /// <exception cref="CoordinationException">
    /// The server failed, or three drawn ids collided with other rows; the call did not apply.
    /// </exception>
    public async ValueTask<long> ResolveAsync(
        Keyspace keyspace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        if (Ids.TryGetValue(keyspace, out var cached))
        {
            return cached;
        }

        var name = PgKeys.KeyspaceName(keyspace);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = Tokens.NextKeyspaceId();
            var insertStatement = new PgStatement(Insert, candidate, name);
            var selectStatement = new PgStatement(Select, name);
            var ids = await Server
                .ExecuteAsync([insertStatement, selectStatement], ReadIdsAsync, cancellationToken)
                .ConfigureAwait(false);

            if (ids.Length > 0)
            {
                var id = ids[0];
                Ids[keyspace] = id;
                return id;
            }
        }

        throw new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            false,
            $"The keyspace '{name}' got no id after {MaxAttempts} attempts."
        );
    }

    /// <summary>
    /// Forgets the cached id of a keyspace.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public void Forget(Keyspace keyspace)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        Ids.TryRemove(keyspace, out _);
    }

    /// <summary>
    /// Forgets every cached id.
    /// </summary>
    public void Clear()
    {
        Ids.Clear();
    }

    /// <summary>
    /// Runs an operation with a keyspace's id; when the operation finds the keyspace's row missing, resolves the id
    /// again and runs it once more.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="operation"/> returns.</typeparam>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="operation">The operation, which takes the keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="operation"/> returned.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the operation is null.</exception>
    /// <exception cref="CoordinationException">
    /// The server failed, or the row was missing on both runs, which did not apply.
    /// </exception>
    public async ValueTask<T> RunAsync<T>(
        Keyspace keyspace,
        Func<long, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(operation);

        var id = await ResolveAsync(keyspace, cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(id, cancellationToken).ConfigureAwait(false);
        }
        catch (PgKeyspaceMissingException first)
        {
            Forget(keyspace);
            id = await ResolveAsync(keyspace, cancellationToken).ConfigureAwait(false);

            try
            {
                return await operation(id, cancellationToken).ConfigureAwait(false);
            }
            catch (PgKeyspaceMissingException)
            {
                Forget(keyspace);
                var name = PgKeys.KeyspaceName(keyspace);
                throw new CoordinationException(
                    CoordinationErrorCodes.Unavailable,
                    false,
                    $"The keyspace '{name}' row was missing again after it was resolved again.",
                    first
                );
            }
        }
    }

    /// <summary>
    /// Runs an operation with a keyspace's id; when the operation finds the keyspace's row missing, resolves the id
    /// again and runs it once more.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="operation">The operation, which takes the keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the operation has.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the operation is null.</exception>
    /// <exception cref="CoordinationException">
    /// The server failed, or the row was missing on both runs, which did not apply.
    /// </exception>
    public async ValueTask RunAsync(
        Keyspace keyspace,
        Func<long, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RunAsync<bool>(
                keyspace,
                (id, token) => CompleteAsync(operation, id, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
