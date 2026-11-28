// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// A short transaction on one connection, whose first batch begins it and reads, and whose second batch writes and
/// commits it.
/// </summary>
/// <param name="server">The server that created the transaction.</param>
/// <param name="connection">The open connection, which the server disposes.</param>
/// <param name="isolation">The isolation of the transaction.</param>
internal sealed class PgTransaction(
    PgServer server,
    NpgsqlConnection connection,
    PgIsolation isolation
)
{
    /// <summary>
    /// Gets the statement that commits the transaction.
    /// </summary>
    private static PgStatement Commit { get; } = new("COMMIT");

    /// <summary>
    /// Gets the statement that rolls the transaction back.
    /// </summary>
    private static PgStatement Rollback { get; } = new("ROLLBACK");

    /// <summary>
    /// Gets the statement that begins a read-write transaction.
    /// </summary>
    private static PgStatement BeginReadCommitted { get; } =
        new("BEGIN ISOLATION LEVEL READ COMMITTED");

    /// <summary>
    /// Gets the statement that begins a snapshot read.
    /// </summary>
    private static PgStatement BeginRepeatableReadReadOnly { get; } =
        new("BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY");

    /// <summary>
    /// Gets the server that created the transaction.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the open connection that the transaction runs on.
    /// </summary>
    private NpgsqlConnection Connection { get; } =
        connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Gets the isolation of the transaction.
    /// </summary>
    private PgIsolation Isolation { get; } = isolation;

    /// <summary>
    /// Gets whether the step that commits the transaction was sent.
    /// </summary>
    public bool CommitSent { get; private set; }

    /// <summary>
    /// Gets whether the transaction was committed or rolled back.
    /// </summary>
    public bool Completed { get; private set; }

    /// <summary>
    /// Begins the transaction, sets its timeouts and runs the first batch of reads.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="statements">The statements that follow the timeouts statement.</param>
    /// <param name="read">Reads the results of the statements.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    public async ValueTask<T> FirstAsync<T>(
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(read);

        var begin =
            Isolation == PgIsolation.RepeatableReadReadOnly
                ? BeginRepeatableReadReadOnly
                : BeginReadCommitted;

        var skipTimeouts = PgServer.SkipTimeouts(read);
        var result = await PgServer
            .RunBatchAsync(
                Connection,
                [begin, Server.Timeouts, .. statements],
                skipTimeouts,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (Server.AfterFirstBatch is { } afterFirstBatch)
        {
            await afterFirstBatch(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Runs a further batch of reads inside the transaction.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="statements">The statements of the batch.</param>
    /// <param name="read">Reads the results of the statements.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    public ValueTask<T> NextAsync<T>(
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(read);

        return PgServer.RunBatchAsync(Connection, statements, read, cancellationToken);
    }

    /// <summary>
    /// Sends writes followed by <c>COMMIT</c>, which completes the transaction.
    /// </summary>
    /// <param name="statements">The writes.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the transaction committed.</returns>
    public async ValueTask CommitAsync(
        IReadOnlyList<PgStatement> statements,
        CancellationToken cancellationToken
    )
    {
        await CommitAsync(
                statements,
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends writes followed by <c>COMMIT</c>, which completes the transaction, and reads the writes' results.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="statements">The writes.</param>
    /// <param name="read">Reads the results of the writes.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    public async ValueTask<T> CommitAsync<T>(
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(read);

        CommitSent = true;
        var result = await PgServer
            .RunBatchAsync(Connection, [.. statements, Commit], read, cancellationToken)
            .ConfigureAwait(false);

        Completed = true;
        return result;
    }

    /// <summary>
    /// Sends <c>ROLLBACK</c>, which completes the transaction.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the transaction was rolled back.</returns>
    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        await PgServer
            .RunBatchAsync(
                Connection,
                [Rollback],
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        Completed = true;
    }
}
