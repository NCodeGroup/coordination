// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Translates the failures of the Npgsql client into coordination failures that say whether the call may have taken
/// effect.
/// </summary>
internal static class PgFailures
{
    /// <summary>
    /// The state of a foreign key violation, which says a statement's keyspace row is missing.
    /// </summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>
    /// The state of a session that ended while idle in a transaction.
    /// </summary>
    private const string IdleInTransactionTimeout = "25P03";

    /// <summary>
    /// The state of a connection that failed while a transaction ended.
    /// </summary>
    private const string ConnectionFailureDuringTransaction = "08007";

    /// <summary>
    /// Returns whether an exception is a failure of the client or the server, which <see cref="Translate"/> handles.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for an <see cref="NpgsqlException"/>, which includes a <see cref="PostgresException"/>.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static bool IsClientFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is NpgsqlException;
    }

    /// <summary>
    /// Returns whether an exception says the server ended the connection or the connection failed, which leaves the
    /// idle connections of the pool to the same server dead.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>
    /// True for a <see cref="PostgresException"/> whose severity is <c>FATAL</c> or <c>PANIC</c>, and for any other
    /// <see cref="NpgsqlException"/>; false otherwise, <see cref="OperationCanceledException"/> included.
    /// </returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static bool IsConnectionLost(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            PostgresException postgres => postgres.InvariantSeverity is "FATAL" or "PANIC",
            NpgsqlException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns whether an exception says a statement found its keyspace's row missing.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for a <see cref="PostgresException"/> of a foreign key violation.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static bool IsKeyspaceMissing(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is PostgresException { SqlState: ForeignKeyViolation };
    }

    /// <summary>
    /// Translates a failure of the client into a coordination failure.
    /// </summary>
    /// <param name="exception">The failure of the client or the server.</param>
    /// <param name="committingStepSent">True when the step that commits the call's changes was already sent.</param>
    /// <returns>The coordination failure, with <paramref name="exception"/> as its inner exception.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static CoordinationException Translate(Exception exception, bool committingStepSent)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var code = ClassifyCode(exception);
        var outcomeUnknown = committingStepSent && IsOutcomeUnknown(exception);
        var detail = exception is PostgresException postgres
            ? $"{postgres.SqlState} {postgres.MessageText}"
            : exception.Message;

        return new CoordinationException(
            code,
            outcomeUnknown,
            "The PostgreSQL server call failed: " + detail,
            exception
        );
    }

    /// <summary>
    /// Picks the code of a failure.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The vocabulary code.</returns>
    private static string ClassifyCode(Exception exception)
    {
        if (exception is not PostgresException postgres)
        {
            return CoordinationErrorCodes.Unavailable;
        }

        var state = postgres.SqlState;
        if (state.StartsWith("28", StringComparison.Ordinal) || state == "42501")
        {
            return CoordinationErrorCodes.AccessDenied;
        }

        return state == "22003"
            ? CoordinationErrorCodes.LimitExceeded
            : CoordinationErrorCodes.Unavailable;
    }

    /// <summary>
    /// Returns whether a failure that came after the committing step was sent leaves the outcome unknown.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>False for a server error that says the changes did not commit, true otherwise.</returns>
    private static bool IsOutcomeUnknown(Exception exception)
    {
        if (exception is not PostgresException postgres)
        {
            return true;
        }

        if (postgres.SqlState == IdleInTransactionTimeout)
        {
            return false;
        }

        return postgres.InvariantSeverity != "ERROR"
            || postgres.SqlState == ConnectionFailureDuringTransaction;
    }
}
