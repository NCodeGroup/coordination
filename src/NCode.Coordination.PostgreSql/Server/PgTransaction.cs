// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// A short transaction on one connection, whose first batch begins it and reads, and whose second batch writes and
/// commits it.
/// </summary>
internal sealed class PgTransaction
{
    private static readonly PgStatement Commit = new("COMMIT");
    private static readonly PgStatement Rollback = new("ROLLBACK");
    private static readonly PgStatement BeginReadCommitted = new(
        "BEGIN ISOLATION LEVEL READ COMMITTED"
    );
    private static readonly PgStatement BeginRepeatableReadReadOnly = new(
        "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY"
    );

    private readonly PgServer _server;
    private readonly NpgsqlConnection _connection;
    private readonly PgIsolation _isolation;

    /// <summary>
    /// Creates a transaction on an open connection, which the server disposes.
    /// </summary>
    /// <param name="server">The server that created the transaction.</param>
    /// <param name="connection">The open connection.</param>
    /// <param name="isolation">The isolation of the transaction.</param>
    internal PgTransaction(PgServer server, NpgsqlConnection connection, PgIsolation isolation)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(connection);

        _server = server;
        _connection = connection;
        _isolation = isolation;
    }

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
            _isolation == PgIsolation.RepeatableReadReadOnly
                ? BeginRepeatableReadReadOnly
                : BeginReadCommitted;

        var result = await PgServer
            .RunBatchAsync(
                _connection,
                [begin, _server.Timeouts, .. statements],
                PgServer.SkipTimeouts(read),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (_server.AfterFirstBatch is { } afterFirstBatch)
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

        return PgServer.RunBatchAsync(_connection, statements, read, cancellationToken);
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
            .RunBatchAsync(_connection, [.. statements, Commit], read, cancellationToken)
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
                _connection,
                [Rollback],
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        Completed = true;
    }
}
