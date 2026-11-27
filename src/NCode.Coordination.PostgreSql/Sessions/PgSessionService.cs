// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sessions;

/// <summary>
/// The sessions of the PostgreSQL provider: each session is a row of the keyspace's <c>sessions</c> table with its
/// time to live and its deadline, and ending one, at a close, at a write that meets it expired or at the sweep,
/// deletes its row and takes one revision. A wait for a session's end polls until the session has ended or the caller
/// cancels.
/// </summary>
/// <param name="table">The record table, whose keyspaces' ids and transactions the service shares.</param>
/// <param name="tokens">The source of session ids.</param>
/// <exception cref="ArgumentNullException">The table or the tokens are null.</exception>
internal sealed class PgSessionService(PgRecordTable table, IPgTokens tokens) : ISessionService
{
    /// <summary>The template of the statement that inserts a session's row.</summary>
    private const string InsertSession = """
        INSERT INTO {schema}.sessions (keyspace_id, id, ttl_us, deadline)
        VALUES ($1, $2, $3, clock_timestamp() + $3 * interval '1 microsecond')
        """;

    /// <summary>The template of the statement that renews a live session's deadline by its whole time to live.</summary>
    private const string RenewSession = """
        WITH now AS (SELECT clock_timestamp() AS t)
        UPDATE {schema}.sessions
        SET deadline = (SELECT t FROM now) + ttl_us * interval '1 microsecond'
        WHERE keyspace_id = $1 AND id = $2 AND deadline > (SELECT t FROM now)
        RETURNING ttl_us
        """;

    /// <summary>The template of the statement that tells whether a session is no longer live.</summary>
    private const string SelectEnded = """
        SELECT NOT EXISTS (
            SELECT 1 FROM {schema}.sessions
            WHERE keyspace_id = $1 AND id = $2 AND deadline > clock_timestamp())
        """;

    /// <summary>
    /// Gets the record table, whose keyspaces' ids and transactions the service shares.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the source of session ids.
    /// </summary>
    private IPgTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <summary>Gets the statement that inserts a session's row.</summary>
    private string InsertSql { get; } = table.Settings.Schema.Apply(InsertSession);

    /// <summary>Gets the statement that renews a live session's deadline.</summary>
    private string RenewSql { get; } = table.Settings.Schema.Apply(RenewSession);

    /// <summary>Gets the statement that tells whether a session is no longer live.</summary>
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

        var token = Tokens.Next(PgTokenPrefixes.Session);
        var microseconds = PgCall.ToMicroseconds(timeToLive);

        await Table
            .Keyspaces.RunAsync(
                keyspace,
                async (id, runToken) =>
                    await Table
                        .Server.ExecuteAsync(
                            [
                                new PgStatement(
                                    InsertSql,
                                    PgArgument.Of(id),
                                    PgArgument.Of(token),
                                    PgArgument.Of(microseconds)
                                ),
                            ],
                            static (_, _) => ValueTask.FromResult(true),
                            runToken
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new SessionGrant(new SessionId(keyspace, token), timeToLive);
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
            .Keyspaces.RunAsync<KeepAliveResult>(
                session.Keyspace,
                async (id, token) =>
                {
                    var renewed = await Table
                        .Server.ExecuteAsync(
                            [
                                new PgStatement(
                                    RenewSql,
                                    PgArgument.Of(id),
                                    PgArgument.Of(session.Value)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return renewed.Length == 0
                        ? KeepAliveResult.Ended
                        : KeepAliveResult.Renewed(
                            new TimeToLive(TimeSpan.FromTicks(renewed[0] * 10))
                        );
                },
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
            .Keyspaces.RunAsync(
                session.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(
                                        transaction,
                                        id,
                                        [],
                                        [session.Value],
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                if (start.Sessions.ContainsKey(session.Value))
                                {
                                    change.EndSession(session.Value);
                                }

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return true;
                            },
                            token
                        )
                        .ConfigureAwait(false),
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

        await PgPolling
            .UntilAsync(token => HasEndedAsync(session, token), cancellationToken)
            .ConfigureAwait(false);
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
            .Keyspaces.RunAsync(
                session.Keyspace,
                async (id, token) =>
                {
                    var ended = await Table
                        .Server.ExecuteAsync(
                            [
                                new PgStatement(
                                    EndedSql,
                                    PgArgument.Of(id),
                                    PgArgument.Of(session.Value)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetBoolean(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return ended.Length > 0 && ended[0];
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
