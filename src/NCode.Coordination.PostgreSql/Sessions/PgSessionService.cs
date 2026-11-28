// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Sessions;

/// <summary>
/// The sessions of the PostgreSQL provider: each session is a row of the keyspace's <c>sessions</c> table with its
/// time to live and its deadline, and ending one, at a close, at a write that meets it expired or at the sweep,
/// deletes its row and takes one revision. A wait for a session's end polls until the session has ended or the caller
/// cancels.
/// </summary>
/// <param name="table">The record table, whose keyspaces' ids and transactions the service shares.</param>
/// <param name="tokens">The source of session ids.</param>
/// <param name="polling">The poll that a wait for a session's end runs on the provider's clock.</param>
/// <exception cref="ArgumentNullException">The table, the tokens or the polling are null.</exception>
internal sealed class PgSessionService(PgRecordTable table, IPgTokens tokens, PgPolling polling)
    : ISessionService
{
    /// <summary>
    /// The template of the statement that inserts a session's row.
    /// </summary>
    private const string InsertSession = """
        INSERT INTO {schema}.sessions (keyspace_id, id, ttl_us, deadline)
        VALUES ($1, $2, $3, clock_timestamp() + $3 * interval '1 microsecond')
        """;

    /// <summary>
    /// The template of the statement that renews a live session's deadline by its whole time to live.
    /// </summary>
    private const string RenewSession = """
        WITH now AS (SELECT clock_timestamp() AS t)
        UPDATE {schema}.sessions
        SET deadline = (SELECT t FROM now) + ttl_us * interval '1 microsecond'
        WHERE keyspace_id = $1 AND id = $2 AND deadline > (SELECT t FROM now)
        RETURNING ttl_us
        """;

    /// <summary>
    /// The template of the statement that tells whether a session is no longer live.
    /// </summary>
    private const string SelectEnded = """
        SELECT NOT EXISTS (
            SELECT 1 FROM {schema}.sessions
            WHERE keyspace_id = $1 AND id = $2 AND deadline > clock_timestamp())
        """;

    /// <summary>
    /// Reads the time to live of a renewed session's row, in whole microseconds.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The session's whole time to live in microseconds.</returns>
    private static long ReadMicroseconds(NpgsqlDataReader reader)
    {
        return reader.GetInt64(0);
    }

    /// <summary>
    /// Reads whether a session is no longer live.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>True when the session is not live.</returns>
    private static bool ReadEnded(NpgsqlDataReader reader)
    {
        return reader.GetBoolean(0);
    }

    /// <summary>
    /// Reads the result of <see cref="RenewSession"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The time to live in microseconds of each session that was renewed.</returns>
    private static async ValueTask<ImmutableArray<long>> ReadRenewedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadMicroseconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the result of <see cref="SelectEnded"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Whether the session is not live, in a single item.</returns>
    private static async ValueTask<ImmutableArray<bool>> ReadEndedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadEnded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the result of a keep-alive from the rows that the renewal returned.
    /// </summary>
    /// <param name="renewed">The time to live in microseconds of each session that was renewed.</param>
    /// <returns>The result that says the session ended, or that it was renewed for its time to live.</returns>
    private static KeepAliveResult ToKeepAliveResult(ImmutableArray<long> renewed)
    {
        if (renewed.Length == 0)
        {
            return KeepAliveResult.Ended;
        }

        var renewedFor = PgCall.FromMicroseconds(renewed[0]);
        var timeToLive = new TimeToLive(renewedFor);
        return KeepAliveResult.Renewed(timeToLive);
    }

    /// <summary>
    /// Gets the record table, whose keyspaces' ids and transactions the service shares.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the source of session ids.
    /// </summary>
    private IPgTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <summary>
    /// Gets the poll that a wait for a session's end runs on the provider's clock.
    /// </summary>
    private PgPolling Polling { get; } =
        polling ?? throw new ArgumentNullException(nameof(polling));

    /// <summary>
    /// Gets the statement that inserts a session's row.
    /// </summary>
    private string InsertSql { get; } = table.Settings.Schema.Apply(InsertSession);

    /// <summary>
    /// Gets the statement that renews a live session's deadline.
    /// </summary>
    private string RenewSql { get; } = table.Settings.Schema.Apply(RenewSession);

    /// <summary>
    /// Gets the statement that tells whether a session is no longer live.
    /// </summary>
    private string EndedSql { get; } = table.Settings.Schema.Apply(SelectEnded);

    /// <inheritdoc />
    public async ValueTask<SessionGrant> OpenAsync(
        Keyspace keyspace,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(timeToLive);
        PgCall.Check(options, cancellationToken);

        var sessionToken = Tokens.Next(PgTokenPrefixes.Session);
        var microseconds = PgCall.ToMicroseconds(timeToLive);

        await Table
            .RunInKeyspaceAsync(
                keyspace,
                (id, token) => InsertInKeyspaceAsync(id, sessionToken, microseconds, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var sessionId = new SessionId(keyspace, sessionToken);
        return new SessionGrant(sessionId, timeToLive);
    }

    /// <inheritdoc />
    public async ValueTask<KeepAliveResult> KeepAliveAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        PgCall.Check(options, cancellationToken);

        return await Table
            .RunInKeyspaceAsync<KeepAliveResult>(
                session.Keyspace,
                (id, token) => RenewInKeyspaceAsync(id, session, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CloseAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        PgCall.Check(options, cancellationToken);

        await Table
            .RunInKeyspaceAsync(
                session.Keyspace,
                (id, token) => CloseInKeyspaceAsync(id, session, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        PgCall.Check(options, cancellationToken);

        await Polling
            .UntilAsync(token => HasEndedAsync(session, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a session's row in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="sessionToken">The new session's id.</param>
    /// <param name="microseconds">The session's time to live in whole microseconds.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    private async ValueTask InsertInKeyspaceAsync(
        long id,
        string sessionToken,
        long microseconds,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(InsertSql, id, sessionToken, microseconds);

        await Table
            .ExecuteAsync([statement], PgResults.IgnoreAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Renews a session's deadline in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result that says the session ended, or that it was renewed.</returns>
    private async ValueTask<KeepAliveResult> RenewInKeyspaceAsync(
        long id,
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(RenewSql, id, session.Value);

        var renewed = await Table
            .ExecuteAsync([statement], ReadRenewedAsync, cancellationToken)
            .ConfigureAwait(false);

        return ToKeepAliveResult(renewed);
    }

    /// <summary>
    /// Ends a session in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    private async ValueTask CloseInKeyspaceAsync(
        long id,
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        await Table
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => CloseInTransactionAsync(transaction, id, session, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, ends the session when its row exists and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>True, once the change has committed.</returns>
    private async ValueTask<bool> CloseInTransactionAsync(
        PgTransaction transaction,
        long id,
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        var scope = new PgRecordScope(id, [], [session.Value]);
        var start = await Table
            .BeginAsync(transaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        if (start.Sessions.ContainsKey(session.Value))
        {
            change.EndSession(session.Value);
        }

        await Table.CommitAsync(transaction, change, id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Asks the server whether a session has ended.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when the session is not live.</returns>
    /// <exception cref="CoordinationException">The call failed.</exception>
    private async ValueTask<bool> HasEndedAsync(
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .RunInKeyspaceAsync<bool>(
                session.Keyspace,
                (id, token) => ReadEndedInKeyspaceAsync(id, session, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the server whether a session of a keyspace has ended.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the session is not live.</returns>
    private async ValueTask<bool> ReadEndedInKeyspaceAsync(
        long id,
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(EndedSql, id, session.Value);

        var ended = await Table
            .ExecuteAsync([statement], ReadEndedAsync, cancellationToken)
            .ConfigureAwait(false);

        return ended.Length > 0 && ended[0];
    }
}
