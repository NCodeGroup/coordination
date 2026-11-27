// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Security.Cryptography;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Keys;

/// <summary>
/// Resolves the id of each keyspace's row and caches it, and runs an operation again when its keyspace's row was
/// found missing.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="schema">The schema that holds the keyspaces' table.</param>
internal sealed class PgKeyspaces(PgServer server, PgSchemaName schema)
{
    /// <summary>The most times that <see cref="ResolveAsync"/> draws an id before it gives up.</summary>
    private const int MaxAttempts = 3;

    /// <summary>The statement that inserts a keyspace's row unless its id or its name is taken.</summary>
    private const string InsertSql = """
        INSERT INTO {schema}.keyspaces (id, name) VALUES ($1, $2) ON CONFLICT DO NOTHING
        """;

    /// <summary>The statement that reads the id of a keyspace's row.</summary>
    private const string SelectSql = """
        SELECT id FROM {schema}.keyspaces WHERE name = $1
        """;

    private readonly ConcurrentDictionary<Keyspace, long> _ids = new();

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

        if (_ids.TryGetValue(keyspace, out var cached))
        {
            return cached;
        }

        var name = PgKeys.KeyspaceName(keyspace);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = DrawId();
            var ids = await server
                .ExecuteAsync(
                    [
                        new PgStatement(Insert, PgArgument.Of(candidate), PgArgument.Of(name)),
                        new PgStatement(Select, PgArgument.Of(name)),
                    ],
                    async (results, token) =>
                        await results
                            .ReadAsync(reader => reader.GetInt64(0), token)
                            .ConfigureAwait(false),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (ids.Length > 0)
            {
                var id = ids[0];
                _ids[keyspace] = id;
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

        _ids.TryRemove(keyspace, out _);
    }

    /// <summary>
    /// Forgets every cached id.
    /// </summary>
    public void Clear()
    {
        _ids.Clear();
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
                throw new CoordinationException(
                    CoordinationErrorCodes.Unavailable,
                    false,
                    $"The keyspace '{PgKeys.KeyspaceName(keyspace)}' row was missing again after it was resolved again.",
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

        await RunAsync(
                keyspace,
                async (id, token) =>
                {
                    await operation(id, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Draws a random id that is not zero.
    /// </summary>
    /// <returns>The id.</returns>
    private static long DrawId()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var id = BitConverter.ToInt64(bytes);
            if (id != 0)
            {
                return id;
            }
        }
    }
}
