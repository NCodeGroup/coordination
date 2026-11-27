// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

// The server's clock advances only by waiting, so the target's clock waits.
internal static class PgConformance
{
    /// <summary>
    /// Gets the settings of the provider under test: the module's defaults with a sweep and a log check every second,
    /// so the tests that wait for expiry see rows removed soon.
    /// </summary>
    internal static PgSettings Settings { get; } =
        PgSettings.Default with
        {
            SweepInterval = TimeSpan.FromSeconds(1),
            CheckInterval = TimeSpan.FromSeconds(1),
        };

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

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "conformance-" + Guid.NewGuid().ToString("N"),
        };

        var provider = new PostgreSqlProvider(
            PgSource.FromConnectionString(builder.ConnectionString),
            profile,
            Settings
        );

        try
        {
            await provider.ProbeAsync(cancellationToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return new ConformanceTarget(provider) { AdvanceClock = WaitAsync };
    }

    private static ValueTask WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(duration, cancellationToken);
        return new ValueTask(delay);
    }
}
