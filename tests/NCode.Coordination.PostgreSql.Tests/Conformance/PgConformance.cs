// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

// The server's clock advances only by waiting, so the target's clock waits.
// The target drops only its own provider's connections, by application name, since every test shares the servers.
internal static class PgConformance
{
    /// <summary>
    /// The statement that lists the backends of the provider under test.
    /// </summary>
    private const string SelectPidsSql =
        "SELECT array_agg(pid) FROM pg_stat_activity WHERE application_name = $1";

    /// <summary>
    /// The statement that ends the backends.
    /// </summary>
    private const string TerminateSql =
        "SELECT count(pg_terminate_backend(p)) FROM unnest($1::int[]) AS p";

    /// <summary>
    /// The statement that counts the backends that have not exited.
    /// </summary>
    private const string CountSql =
        "SELECT count(*) FROM pg_stat_activity WHERE pid = ANY($1::int[])";

    /// <summary>
    /// Gets the settings of the provider under test: the module's defaults with a sweep and a log check every second,
    /// so the tests that wait for expiry see rows removed soon.
    /// </summary>
    internal static PgSettings Settings { get; } = CreateSettings();

    /// <summary>
    /// Gets how long a drop waits for the ended backends to exit.
    /// </summary>
    private static TimeSpan DropLimit { get; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the time between the checks that the ended backends have exited.
    /// </summary>
    private static TimeSpan DropPollInterval { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Builds the settings of the provider under test.
    /// </summary>
    /// <returns>The module's defaults with a sweep and a log check every second.</returns>
    private static PgSettings CreateSettings()
    {
        var interval = TimeSpan.FromSeconds(1);

        return PgSettings.Default with
        {
            SweepInterval = interval,
            CheckInterval = interval,
        };
    }

    /// <summary>
    /// Creates the target of one conformance test: a provider connected to a server under an application name of its
    /// own, and a clock that advances by waiting.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="profile">The profile of the provider under test.</param>
    /// <param name="cancellationToken">The token that cancels the creation.</param>
    /// <returns>The target of the test.</returns>
    public static async ValueTask<ConformanceTarget> CreateTargetAsync(
        string connectionString,
        PostgreSqlProfile profile,
        CancellationToken cancellationToken
    )
    {
        Assert.SkipUnless(
            PgServers.Enabled,
            $"Set {PgServers.EnvironmentVariable}=docker to run the tests against PostgreSQL in Docker."
        );

        var applicationName = "conformance-" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
        };

        var source = PgSource.FromConnectionString(builder.ConnectionString);
        var provider = new PostgreSqlProvider(source, profile, Settings);

        try
        {
            await provider.ProbeAsync(cancellationToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return new ConformanceTarget(provider)
        {
            AdvanceClock = WaitAsync,
            DropConnections = ct => DropConnectionsAsync(connectionString, applicationName, ct),
        };
    }

    /// <summary>
    /// Ends every backend that the provider under test holds, and waits until each has exited, so a test sees the
    /// provider recover from a dropped connection without disturbing the other tests that share the servers.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="applicationName">The application name that only the provider under test uses.</param>
    /// <param name="cancellationToken">The token that cancels the drop.</param>
    /// <returns>A task that completes when every backend of the provider has exited.</returns>
    private static async ValueTask DropConnectionsAsync(
        string connectionString,
        string applicationName,
        CancellationToken cancellationToken
    )
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "conformance-drop",
            Pooling = false,
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var select = new NpgsqlCommand(SelectPidsSql, connection);
        var applicationParameter = new NpgsqlParameter { Value = applicationName };
        select.Parameters.Add(applicationParameter);
        var pids = await select.ExecuteScalarAsync(cancellationToken) as int[];

        Assert.True(
            pids is { Length: > 0 },
            "The drop found no connection of the provider to end."
        );

        await using var terminate = new NpgsqlCommand(TerminateSql, connection);
        var terminateParameter = new NpgsqlParameter { Value = pids };
        terminate.Parameters.Add(terminateParameter);
        await terminate.ExecuteScalarAsync(cancellationToken);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < DropLimit)
        {
            await using var count = new NpgsqlCommand(CountSql, connection);
            var countParameter = new NpgsqlParameter { Value = pids };
            count.Parameters.Add(countParameter);
            var remaining = await count.ExecuteScalarAsync(cancellationToken);
            if (Convert.ToInt64(remaining, CultureInfo.InvariantCulture) == 0)
            {
                return;
            }

            await Task.Delay(DropPollInterval, cancellationToken);
        }

        var seconds = DropLimit.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        Assert.Fail(
            $"The backends of the provider were still running {seconds} seconds after they were ended."
        );
    }

    private static ValueTask WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(duration, cancellationToken);
        return new ValueTask(delay);
    }
}
