// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(NCode.Coordination.PostgreSql.Tests.Servers.PgServers))]

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// Starts PostgreSQL 19 and PostgreSQL 15 with durable settings, and one PostgreSQL 19 with the module's unsafe
/// defaults, in Docker for the whole test run, when <see cref="EnvironmentVariable"/> is <c>docker</c>.
/// </summary>
public sealed class PgServers : IAsyncLifetime
{
    /// <summary>The environment variable that turns the server tests on when its value is <c>docker</c>.</summary>
    public const string EnvironmentVariable = "NCODE_COORDINATION_POSTGRESQL_SERVERS";

    /// <summary>The PostgreSQL 19 image the tests run against, pinned by its tag and the digest of its multi-architecture index.</summary>
    public const string Image19 =
        "postgres:19.0@sha256:4ea94de574c17f0372722806c949757050fbb1cd7648410fe45d6bba275cc00e";

    /// <summary>The PostgreSQL 15 image the tests run against, pinned by its tag and the digest of its multi-architecture index.</summary>
    public const string Image15 =
        "postgres:15.20@sha256:868b5ee219026c0f34c99d696d4494ccb3705cf1a18bed1e0b9de64d9c011ccd";

    /// <summary>The name of the non-superuser role that the conformance tests connect as.</summary>
    public const string Role = "coordinator";

    /// <summary>The password of <see cref="Role"/>.</summary>
    public const string RolePassword = "coordinator";

    /// <summary>The name of the database that the servers create and the tests use.</summary>
    private const string Database = "postgres";

    /// <summary>Gets a value indicating whether the server tests run.</summary>
    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) == "docker";

    /// <summary>
    /// Gets the arguments that start a server with the durability settings that the provider's probe requires, which
    /// override the module's defaults because the last <c>-c</c> wins.
    /// </summary>
    internal static IReadOnlyList<string> DurableCommand { get; } =
    ["-c", "fsync=on", "-c", "full_page_writes=on", "-c", "synchronous_commit=on"];

    /// <summary>Gets the connection string of PostgreSQL 19 as <see cref="Role"/>, empty until it is started.</summary>
    public string PostgreSql19 { get; private set; } = "";

    /// <summary>Gets the connection string of PostgreSQL 15 as <see cref="Role"/>, empty until it is started.</summary>
    public string PostgreSql15 { get; private set; } = "";

    /// <summary>Gets the connection string of PostgreSQL 19 as <c>postgres</c>, empty until it is started.</summary>
    public string Admin19 { get; private set; } = "";

    /// <summary>Gets the connection string of PostgreSQL 15 as <c>postgres</c>, empty until it is started.</summary>
    public string Admin15 { get; private set; } = "";

    /// <summary>
    /// Gets the connection string, as <c>postgres</c>, of the PostgreSQL 19 server that runs without
    /// <see cref="DurableCommand"/>, empty until it is started.
    /// </summary>
    public string Unsafe19 { get; private set; } = "";

    private PostgreSqlContainer? Container19 { get; set; }

    private PostgreSqlContainer? Container15 { get; set; }

    private PostgreSqlContainer? UnsafeContainer19 { get; set; }

    /// <summary>
    /// Copies a connection string with another user and password.
    /// </summary>
    /// <param name="adminConnectionString">The connection string to copy.</param>
    /// <param name="role">The user name of the copy.</param>
    /// <param name="password">The password of the copy.</param>
    /// <returns>The connection string of the copy.</returns>
    internal static string AsRole(string adminConnectionString, string role, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = role,
            Password = password,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Runs one command on a server through a connection of its own.
    /// </summary>
    /// <param name="adminConnectionString">The connection string of the user that runs the command.</param>
    /// <param name="sql">The text of the command.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>A task that completes when the command has run.</returns>
    internal static async ValueTask ExecuteAsAdminAsync(
        string adminConnectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a server whose data directory is in memory: it still runs with <c>fsync</c> on when
    /// <paramref name="durable"/> is true, which the probe checks, but each commit's flush costs a memory write instead
    /// of a disk write, so the suites' long runs of single commits fit their times to live. A test that restarts or
    /// fails a server over needs its data on disk and starts a server of its own.
    /// </summary>
    /// <param name="image">The image of the server.</param>
    /// <param name="durable">Whether the server runs with <see cref="DurableCommand"/>.</param>
    /// <returns>The container, not yet started.</returns>
    private static PostgreSqlContainer CreateContainer(string image, bool durable)
    {
        var builder = new PostgreSqlBuilder(image)
            .WithDatabase(Database)
            .WithTmpfsMount("/var/lib/postgresql");
        if (durable)
        {
            builder = builder.WithCommand([.. DurableCommand]);
        }

        return builder.Build();
    }

    private static async ValueTask CreateRoleAsync(
        string adminConnectionString,
        CancellationToken cancellationToken
    )
    {
        await ExecuteAsAdminAsync(
            adminConnectionString,
            $"CREATE ROLE {Role} LOGIN PASSWORD '{RolePassword}'",
            cancellationToken
        );
        await ExecuteAsAdminAsync(
            adminConnectionString,
            $"GRANT CREATE ON DATABASE {Database} TO {Role}",
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var container19 = CreateContainer(Image19, durable: true);
        var container15 = CreateContainer(Image15, durable: true);
        var unsafeContainer19 = CreateContainer(Image19, durable: false);
        Container19 = container19;
        Container15 = container15;
        UnsafeContainer19 = unsafeContainer19;

        try
        {
            await Task.WhenAll(
                container19.StartAsync(CancellationToken.None),
                container15.StartAsync(CancellationToken.None),
                unsafeContainer19.StartAsync(CancellationToken.None)
            );

            var admin19 = container19.GetConnectionString();
            var admin15 = container15.GetConnectionString();
            await CreateRoleAsync(admin19, CancellationToken.None);
            await CreateRoleAsync(admin15, CancellationToken.None);

            Admin19 = admin19;
            Admin15 = admin15;
            Unsafe19 = unsafeContainer19.GetConnectionString();
            PostgreSql19 = AsRole(admin19, Role, RolePassword);
            PostgreSql15 = AsRole(admin15, Role, RolePassword);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Container19 is { } container19)
        {
            await container19.DisposeAsync();
            Container19 = null;
        }

        if (Container15 is { } container15)
        {
            await container15.DisposeAsync();
            Container15 = null;
        }

        if (UnsafeContainer19 is { } unsafeContainer19)
        {
            await unsafeContainer19.DisposeAsync();
            UnsafeContainer19 = null;
        }
    }
}
