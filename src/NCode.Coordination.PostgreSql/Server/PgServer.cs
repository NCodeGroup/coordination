// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The seam between the provider and the PostgreSQL server: runs a single statement as one batch, or a short
/// transaction as two, and translates every failure of the client.
/// </summary>
/// <param name="source">The source of the connections.</param>
/// <param name="settings">The settings, whose timeouts every call sends.</param>
internal sealed class PgServer(PgSource source, PgSettings settings)
{
    /// <summary>
    /// The statement that sets the timeouts of the current transaction.
    /// </summary>
    private const string TimeoutsSql = """
        SELECT set_config('statement_timeout', $1, true),
               set_config('lock_timeout', $2, true),
               set_config('idle_in_transaction_session_timeout', $3, true)
        """;

    /// <summary>
    /// Sends statements as one batch on a connection and reads their results.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="connection">The open connection.</param>
    /// <param name="statements">The statements of the batch.</param>
    /// <param name="read">Reads the results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned, after the batch was read to its end.</returns>
    internal static async ValueTask<T> RunBatchAsync<T>(
        NpgsqlConnection connection,
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        await using var batch = new NpgsqlBatch(connection);
        foreach (var statement in statements)
        {
            var command = statement.ToBatchCommand();
            batch.BatchCommands.Add(command);
        }

        await using var reader = await batch
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new PgResults(reader);
        var value = await read(results, cancellationToken).ConfigureAwait(false);
        await results.DrainAsync(cancellationToken).ConfigureAwait(false);
        return value;
    }

    /// <summary>
    /// Wraps a reader so that it first reads the result of the timeouts statement.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="read">Reads the results that follow the timeouts statement's.</param>
    /// <returns>A reader that skips the timeouts row, then calls <paramref name="read"/>.</returns>
    internal static Func<PgResults, CancellationToken, ValueTask<T>> SkipTimeouts<T>(
        Func<PgResults, CancellationToken, ValueTask<T>> read
    )
    {
        return async (results, cancellationToken) =>
        {
            await results.SkipAsync(cancellationToken).ConfigureAwait(false);
            return await read(results, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Creates the statement that sets the timeouts of the current transaction from the settings.
    /// </summary>
    /// <param name="timeoutSettings">The settings that hold the timeouts.</param>
    /// <returns>The statement, whose arguments are the timeouts in milliseconds.</returns>
    /// <exception cref="ArgumentNullException">The settings are null.</exception>
    private static PgStatement CreateTimeouts(PgSettings timeoutSettings)
    {
        ArgumentNullException.ThrowIfNull(timeoutSettings);

        var statementTimeout = ToMilliseconds(timeoutSettings.StatementTimeout);
        var lockTimeout = ToMilliseconds(timeoutSettings.LockTimeout);
        var idleTimeout = ToMilliseconds(timeoutSettings.IdleInTransactionTimeout);

        return new PgStatement(TimeoutsSql, statementTimeout, lockTimeout, idleTimeout);
    }

    /// <summary>
    /// Converts a duration to the text of a timeout setting, in whole milliseconds rounded up.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The milliseconds in invariant decimal followed by <c>ms</c>.</returns>
    private static string ToMilliseconds(TimeSpan duration)
    {
        var milliseconds =
            (duration.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond;
        return milliseconds.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    /// <summary>
    /// Gets the source of the connections.
    /// </summary>
    private PgSource Source { get; } = source;

    /// <summary>
    /// Gets the statement that sets the statement, lock and idle-in-transaction timeouts of the current transaction.
    /// </summary>
    internal PgStatement Timeouts { get; } = CreateTimeouts(settings);

    /// <summary>
    /// Gets or sets a hook that a test sets, which a transaction awaits after its first batch was read.
    /// </summary>
    internal Func<CancellationToken, ValueTask>? AfterFirstBatch { get; set; }

    /// <summary>
    /// Runs statements as one batch, which is one implicit transaction.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="statements">The statements, sent after the timeouts statement.</param>
    /// <param name="read">Reads the results of the statements.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    /// <exception cref="PgKeyspaceMissingException">A statement found its keyspace's row missing.</exception>
    public async ValueTask<T> ExecuteAsync<T>(
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(read);

        var sent = false;
        try
        {
            await using var connection = await Source
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            sent = true;
            var skipTimeouts = SkipTimeouts(read);
            return await RunBatchAsync(
                    connection,
                    [Timeouts, .. statements],
                    skipTimeouts,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception e) when (PgFailures.IsClientFailure(e))
        {
            throw Fail(e, sent);
        }
    }

    /// <summary>
    /// Runs a short transaction on one connection.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="body"/> returns.</typeparam>
    /// <param name="isolation">The transaction's isolation.</param>
    /// <param name="body">Runs the transaction's batches, and completes it.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="body"/> returned.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    /// <exception cref="PgKeyspaceMissingException">A statement found its keyspace's row missing.</exception>
    public async ValueTask<T> TransactAsync<T>(
        PgIsolation isolation,
        Func<PgTransaction, CancellationToken, ValueTask<T>> body,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(body);

        PgTransaction? transaction = null;
        try
        {
            await using var connection = await Source
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            transaction = new PgTransaction(this, connection, isolation);
            var result = await body(transaction, cancellationToken).ConfigureAwait(false);
            if (!transaction.Completed)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception e) when (PgFailures.IsClientFailure(e))
        {
            throw Fail(e, transaction?.CommitSent ?? false);
        }
    }

    /// <summary>
    /// Translates a failure of the client and clears the pool when the connection was lost.
    /// </summary>
    /// <param name="exception">The failure of the client.</param>
    /// <param name="committingStepSent">Whether the step that commits was sent before the failure.</param>
    /// <returns>
    /// The exception to throw: a <see cref="PgKeyspaceMissingException"/> when a statement found its keyspace's row
    /// missing, else the translated <see cref="CoordinationException"/>, which says whether the call may have taken
    /// effect.
    /// </returns>
    private Exception Fail(Exception exception, bool committingStepSent)
    {
        if (PgFailures.IsConnectionLost(exception))
        {
            Source.ClearPool();
        }

        if (PgFailures.IsKeyspaceMissing(exception))
        {
            return new PgKeyspaceMissingException(
                "A statement found its keyspace's row missing.",
                exception
            );
        }

        return PgFailures.Translate(exception, committingStepSent);
    }
}
