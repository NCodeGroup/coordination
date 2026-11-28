// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// Waits for the backends of a server, as <c>pg_stat_activity</c> shows them.
/// </summary>
internal static class PgActivity
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Polls <c>pg_stat_activity</c> every 50 ms for at most 15 seconds, until the count of the backends that match
    /// the wait's condition satisfies it, and fails the test when it never does.
    /// </summary>
    /// <param name="adminConnectionString">The connection string of the user that reads <c>pg_stat_activity</c>.</param>
    /// <param name="wait">What to wait for.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    public static async Task WaitForBackendsAsync(
        string adminConnectionString,
        PgBackendWait wait,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Timeout)
        {
            var count = await CountAsync(connection, wait, cancellationToken);
            if (wait.IsDone(count))
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        Assert.Fail(wait.Failure);
    }

    /// <summary>
    /// Waits until no backend of an application is left.
    /// </summary>
    /// <param name="adminConnectionString">The connection string of the user that reads <c>pg_stat_activity</c>.</param>
    /// <param name="applicationName">The <c>Application Name</c> of the backends.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when no backend is left.</returns>
    public static Task WaitForNoBackendAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    )
    {
        var wait = new PgBackendWait(
            "application_name = $1",
            applicationName,
            count => count == 0,
            "A backend of the application was still running 15 seconds after it was ended."
        );

        return WaitForBackendsAsync(adminConnectionString, wait, cancellationToken);
    }

    /// <summary>
    /// Waits until a backend of an application runs <c>pg_sleep(5)</c>.
    /// </summary>
    /// <param name="adminConnectionString">The connection string of the user that reads <c>pg_stat_activity</c>.</param>
    /// <param name="applicationName">The <c>Application Name</c> of the backends.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when a backend is sleeping.</returns>
    public static Task WaitForSleepingBackendAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    )
    {
        var wait = new PgBackendWait(
            "application_name = $1 AND state = 'active' AND query LIKE '%pg_sleep(5)%'",
            applicationName,
            count => count > 0,
            "No backend of the application ran pg_sleep(5) within 15 seconds."
        );

        return WaitForBackendsAsync(adminConnectionString, wait, cancellationToken);
    }

    /// <summary>
    /// Waits until a statement on a schema waits for a lock.
    /// </summary>
    /// <param name="adminConnectionString">The connection string of the user that reads <c>pg_stat_activity</c>.</param>
    /// <param name="schemaName">The name of the schema that the waiting statement's text contains.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when a statement waits for a lock.</returns>
    public static Task WaitForLockWaitAsync(
        string adminConnectionString,
        string schemaName,
        CancellationToken cancellationToken
    )
    {
        var wait = new PgBackendWait(
            "wait_event_type = 'Lock' AND position($1 in query) > 0",
            schemaName,
            count => count > 0,
            "No statement on the schema waited for a lock within 15 seconds."
        );

        return WaitForBackendsAsync(adminConnectionString, wait, cancellationToken);
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection,
        PgBackendWait wait,
        CancellationToken cancellationToken
    )
    {
        var sql = "SELECT count(*) FROM pg_stat_activity WHERE " + wait.Condition;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = wait.Argument });

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
    }
}
