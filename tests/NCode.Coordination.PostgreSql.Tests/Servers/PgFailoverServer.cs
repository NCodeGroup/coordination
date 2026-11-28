// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// A PostgreSQL 19 primary and a streaming standby of it in one container of one test's own, each on a host port
/// reserved before it starts and published to the port it listens on inside the container.
/// </summary>
/// <param name="container">The container that runs both servers.</param>
/// <param name="primaryPort">The host port of the primary.</param>
/// <param name="standbyPort">The host port of the standby.</param>
internal sealed class PgFailoverServer(IContainer container, int primaryPort, int standbyPort)
    : IAsyncDisposable
{
    /// <summary>
    /// The port on which the primary listens inside the container.
    /// </summary>
    private const ushort PrimaryContainerPort = 5432;

    /// <summary>
    /// The port on which the standby listens inside the container.
    /// </summary>
    private const ushort StandbyContainerPort = 5433;

    /// <summary>
    /// The name of the superuser and of the database that the image creates.
    /// </summary>
    private const string UserName = "postgres";

    /// <summary>
    /// The password of the superuser, which the image sets from its environment.
    /// </summary>
    private const string Password = "postgres";

    /// <summary>
    /// The line that the container's script prints once the standby accepts connections, which the container's wait
    /// strategy watches for.
    /// </summary>
    private const string StandbyReadyMessage = "Standby ready";

    /// <summary>
    /// The argument that makes the primary wait for the standby, named by its <c>application_name</c>, before a commit
    /// is acknowledged.
    /// </summary>
    private const string SynchronousArgument = " -c synchronous_standby_names=standby";

    /// <summary>
    /// The longest that a wait for the standby or for a cut replication lasts before it fails.
    /// </summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The time between two polls of a server.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Starts a primary and a standby of it, which the primary streams to.
    /// </summary>
    /// <param name="synchronous">Whether the primary waits for the standby before it acknowledges a commit.</param>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started servers.</returns>
    public static async ValueTask<PgFailoverServer> StartAsync(
        bool synchronous,
        CancellationToken cancellationToken
    )
    {
        return await PgPorts.StartOnFreePortsAsync(
            2,
            (ports, token) => StartOnPortsAsync(synchronous, ports, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Starts a primary and a standby of it on two host ports.
    /// </summary>
    /// <param name="synchronous">Whether the primary waits for the standby before it acknowledges a commit.</param>
    /// <param name="ports">The host ports of the primary and the standby.</param>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started servers.</returns>
    private static async ValueTask<PgFailoverServer> StartOnPortsAsync(
        bool synchronous,
        IReadOnlyList<int> ports,
        CancellationToken cancellationToken
    )
    {
        var primaryPort = ports[0];
        var standbyPort = ports[1];
        var waitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(StandbyReadyMessage);
        var container = new ContainerBuilder(PgServers.Image19)
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEntrypoint("bash", "-c")
            .WithCommand(Script(synchronous))
            .WithPortBinding(primaryPort, PrimaryContainerPort)
            .WithPortBinding(standbyPort, StandbyContainerPort)
            .WithWaitStrategy(waitStrategy)
            .Build();

        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return new PgFailoverServer(container, primaryPort, standbyPort);
    }

    /// <summary>
    /// Builds the shell script that starts the primary, copies it with <c>pg_basebackup</c> into a standby, starts the
    /// standby and prints the ready message once the standby accepts connections.
    /// </summary>
    /// <param name="synchronous">Whether the primary waits for the standby before it acknowledges a commit.</param>
    /// <returns>The script that the container runs.</returns>
    private static string Script(bool synchronous)
    {
        var synchronousArgument = synchronous ? SynchronousArgument : "";
        return $"""
            set -e
            docker-entrypoint.sh postgres -c wal_level=replica -c max_wal_senders=4{synchronousArgument} &
            until pg_isready -h 127.0.0.1 -p 5432 -U postgres >/dev/null 2>&1; do sleep 0.2; done
            mkdir -p /var/lib/postgresql/standby && chown postgres:postgres /var/lib/postgresql/standby && chmod 700 /var/lib/postgresql/standby
            gosu postgres pg_basebackup -h 127.0.0.1 -p 5432 -U postgres -D /var/lib/postgresql/standby -R -X stream -d 'application_name=standby'
            gosu postgres postgres -D /var/lib/postgresql/standby -p 5433 -c hot_standby=on &
            until pg_isready -h 127.0.0.1 -p 5433 -U postgres >/dev/null 2>&1; do sleep 0.2; done
            echo '{StandbyReadyMessage}'
            wait
            """;
    }

    /// <summary>
    /// Gets the container that runs both servers.
    /// </summary>
    private IContainer Container { get; } = container;

    /// <summary>
    /// Gets the host port of the primary.
    /// </summary>
    private int PrimaryPort { get; } = primaryPort;

    /// <summary>
    /// Gets the host port of the standby.
    /// </summary>
    private int StandbyPort { get; } = standbyPort;

    /// <summary>
    /// Gets the connection string of the primary as <c>postgres</c>.
    /// </summary>
    public string PrimaryAdmin => BuildConnectionString($"{Container.Hostname}:{PrimaryPort}");

    /// <summary>
    /// Gets the connection string of the standby as <c>postgres</c>, which a promotion turns into a primary's.
    /// </summary>
    public string StandbyAdmin => BuildConnectionString($"{Container.Hostname}:{StandbyPort}");

    /// <summary>
    /// Gets the connection string, as <c>postgres</c>, that names both servers and rechecks them every second, so a
    /// data source that asks for a primary finds the one that a promotion makes.
    /// </summary>
    public string ConnectionString =>
        BuildConnectionString(
            $"{Container.Hostname}:{PrimaryPort},{Container.Hostname}:{StandbyPort}"
        );

    /// <summary>
    /// Builds a connection string as <c>postgres</c> that rechecks its hosts every second.
    /// </summary>
    /// <param name="hosts">The value of the <c>Host</c> keyword.</param>
    /// <returns>The connection string.</returns>
    private static string BuildConnectionString(string hosts) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = hosts,
            Username = UserName,
            Password = Password,
            Database = UserName,
            HostRecheckSeconds = 1,
        }.ConnectionString;

    /// <summary>
    /// Runs one command on a server through a connection of its own.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="sql">The text of the command.</param>
    /// <param name="cancellationToken">The token that cancels the command.</param>
    /// <returns>The first column of the first row that the command returns, or <see langword="null"/>.</returns>
    private static ValueTask<object?> ScalarAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    ) => ScalarAsync(connectionString, sql, [], cancellationToken);

    /// <summary>
    /// Runs one command with positional arguments on a server through a connection of its own.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="sql">The text of the command, which names its arguments <c>$1</c>, <c>$2</c> and so on.</param>
    /// <param name="arguments">The values of the command's positional arguments.</param>
    /// <param name="cancellationToken">The token that cancels the command.</param>
    /// <returns>The first column of the first row that the command returns, or <see langword="null"/>.</returns>
    private static async ValueTask<object?> ScalarAsync(
        string connectionString,
        string sql,
        IReadOnlyList<object> arguments,
        CancellationToken cancellationToken
    )
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var argument in arguments)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = argument });
        }

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// Polls until a condition holds.
    /// </summary>
    /// <param name="description">What the condition says, for the failure's message.</param>
    /// <param name="condition">The condition, which a poll evaluates.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition did not hold within the wait limit.</exception>
    private static async ValueTask WaitUntilAsync(
        string description,
        Func<CancellationToken, ValueTask<bool>> condition,
        CancellationToken cancellationToken
    )
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(WaitLimit);

        try
        {
            while (!await condition(limit.Token))
            {
                await Task.Delay(PollInterval, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {WaitLimit} until {description}.");
        }
    }

    /// <summary>
    /// Waits until the standby has replayed everything that the primary has written so far.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the standby has caught up.</returns>
    public async ValueTask WaitForStandbyAsync(CancellationToken cancellationToken)
    {
        var position = await ScalarAsync(
            PrimaryAdmin,
            "SELECT pg_current_wal_lsn()::text",
            cancellationToken
        );
        var lsn = Assert.IsType<string>(position);

        await WaitUntilAsync(
            "the standby replayed the primary's log",
            async token =>
            {
                var caughtUp = await ScalarAsync(
                    StandbyAdmin,
                    "SELECT pg_wal_lsn_diff(pg_last_wal_replay_lsn(), $1::pg_lsn) >= 0",
                    [lsn],
                    token
                );
                return caughtUp is true;
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Cuts the standby's link to the primary, so that what the primary writes afterwards never reaches the standby.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the cut.</param>
    /// <returns>A task that completes when the primary has no standby connected.</returns>
    public async ValueTask CutReplicationAsync(CancellationToken cancellationToken)
    {
        await ScalarAsync(
            StandbyAdmin,
            "ALTER SYSTEM SET primary_conninfo = ''",
            cancellationToken
        );
        await ScalarAsync(StandbyAdmin, "SELECT pg_reload_conf()", cancellationToken);

        await WaitUntilAsync(
            "the primary had no standby connected",
            async token =>
            {
                var count = await ScalarAsync(
                    PrimaryAdmin,
                    "SELECT count(*) FROM pg_stat_replication",
                    token
                );
                return count is 0L;
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Promotes the standby to a primary.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the promotion.</param>
    /// <returns>A task that completes when the standby has been promoted.</returns>
    public async ValueTask PromoteStandbyAsync(CancellationToken cancellationToken)
    {
        var promoted = await ScalarAsync(StandbyAdmin, "SELECT pg_promote()", cancellationToken);
        Assert.True(promoted is true, "The standby was not promoted.");
    }

    /// <summary>
    /// Stops the primary without letting it shut down cleanly, as a crash would.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the stop.</param>
    /// <returns>A task that completes when the primary has stopped.</returns>
    public async ValueTask StopPrimaryAsync(CancellationToken cancellationToken)
    {
        var result = await Container.ExecAsync(
            ["bash", "-c", "gosu postgres pg_ctl -D \"$PGDATA\" stop -m immediate"],
            cancellationToken
        );
        Assert.True(
            result.ExitCode == 0,
            $"The primary did not stop: {result.Stdout} {result.Stderr}"
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
