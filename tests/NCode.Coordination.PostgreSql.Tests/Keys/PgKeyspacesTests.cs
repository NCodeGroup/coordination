// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Moq;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Keys.Tests;

public sealed class PgKeyspacesTests(PgServers servers) : IDisposable
{
    private static async ValueTask WithKeyspacesAsync(
        string connectionString,
        IPgTokens tokens,
        Func<string, PgKeyspaces, ValueTask> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "keyspaces_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        await using var source = PgSource.FromConnectionString(connectionString);
        var server = new PgServer(source, PgSettings.Default);

        try
        {
            await PgMigrations.ApplyAsync(server, schema, cancellationToken);

            var keyspaces = new PgKeyspaces(server, schema, tokens);
            await test(schemaName, keyspaces);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                connectionString,
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE",
                CancellationToken.None
            );
        }
    }

    private static async ValueTask<List<long>> ReadIdsAsync(
        string connectionString,
        string schema,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT id FROM \"{schema}\".keyspaces WHERE name = $1",
            connection
        );
        var parameter = new NpgsqlParameter { Value = name };
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var ids = new List<long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            ids.Add(id);
        }

        return ids;
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region ResolveAsync Tests

    [PgServerFact]
    public async Task ResolveAsync_SameKeyspace_GivesOneId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new PgRandomTokens();
        await WithKeyspacesAsync(
            servers.Admin19,
            tokens,
            async (schema, keyspaces) =>
            {
                var orders = KeySegment.FromString("orders");
                var keyspace = new Keyspace("cell", orders);

                var first = await keyspaces.ResolveAsync(keyspace, cancellationToken);
                var second = await keyspaces.ResolveAsync(keyspace, cancellationToken);
                keyspaces.Clear();
                var third = await keyspaces.ResolveAsync(keyspace, cancellationToken);
                var name = PgKeys.KeyspaceName(keyspace);
                var ids = await ReadIdsAsync(servers.Admin19, schema, name, cancellationToken);

                Assert.NotEqual(0, first);
                Assert.Equal(first, second);
                Assert.Equal(first, third);
                Assert.Equal([first], ids);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ResolveAsync_TwoKeyspaces_GiveDifferentIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new PgRandomTokens();
        await WithKeyspacesAsync(
            servers.Admin19,
            tokens,
            async (_, keyspaces) =>
            {
                var ordersSegment = KeySegment.FromString("orders");
                var invoicesSegment = KeySegment.FromString("invoices");
                var orders = new Keyspace("cell", ordersSegment);
                var invoices = new Keyspace("cell", invoicesSegment);

                var first = await keyspaces.ResolveAsync(orders, cancellationToken);
                var second = await keyspaces.ResolveAsync(invoices, cancellationToken);

                Assert.NotEqual(first, second);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ResolveAsync_EveryDrawnIdTaken_ThrowsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = Mocks.Create<IPgTokens>();
        tokens.Setup(x => x.NextKeyspaceId()).Returns(42).Verifiable();
        await WithKeyspacesAsync(
            servers.Admin19,
            tokens.Object,
            async (_, keyspaces) =>
            {
                var ordersSegment = KeySegment.FromString("orders");
                var invoicesSegment = KeySegment.FromString("invoices");
                var orders = new Keyspace("cell", ordersSegment);
                var invoices = new Keyspace("cell", invoicesSegment);

                var first = await keyspaces.ResolveAsync(orders, cancellationToken);

                var failure = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await keyspaces.ResolveAsync(invoices, cancellationToken)
                );

                Assert.Equal(42, first);
                Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
                Assert.False(failure.OutcomeUnknown);
                var exactlyFour = Times.Exactly(4);
                tokens.Verify(x => x.NextKeyspaceId(), exactlyFour);
            },
            cancellationToken
        );
    }

    #endregion

    #region RunAsync Tests

    [PgServerFact]
    public async Task RunAsync_RowMissing_ResolvesAgainAndRetriesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new PgRandomTokens();
        await WithKeyspacesAsync(
            servers.Admin19,
            tokens,
            async (schema, keyspaces) =>
            {
                var orders = KeySegment.FromString("orders");
                var keyspace = new Keyspace("cell", orders);
                var name = PgKeys.KeyspaceName(keyspace);
                var seen = new List<long>();

                var result = await keyspaces.RunAsync(
                    keyspace,
                    async (id, token) =>
                    {
                        seen.Add(id);
                        if (seen.Count == 1)
                        {
                            await PgServers.ExecuteAsAdminAsync(
                                servers.Admin19,
                                $"DELETE FROM \"{schema}\".keyspaces",
                                token
                            );
                            throw new PgKeyspaceMissingException();
                        }

                        return "done";
                    },
                    cancellationToken
                );

                var ids = await ReadIdsAsync(servers.Admin19, schema, name, cancellationToken);
                var resolved = await keyspaces.ResolveAsync(keyspace, cancellationToken);

                Assert.Equal("done", result);
                Assert.Equal(2, seen.Count);
                Assert.NotEqual(seen[0], seen[1]);
                Assert.Equal([seen[1]], ids);
                Assert.Equal(seen[1], resolved);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task RunAsync_MissingTwice_ThrowsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new PgRandomTokens();
        await WithKeyspacesAsync(
            servers.Admin19,
            tokens,
            async (_, keyspaces) =>
            {
                var orders = KeySegment.FromString("orders");
                var keyspace = new Keyspace("cell", orders);
                var calls = 0;

                ValueTask<string> OperationAsync(long id, CancellationToken token)
                {
                    calls++;
                    var missing = new PgKeyspaceMissingException();
                    return ValueTask.FromException<string>(missing);
                }

                var failure = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await keyspaces.RunAsync(keyspace, OperationAsync, cancellationToken)
                );

                Assert.Equal(2, calls);
                Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
                Assert.False(failure.OutcomeUnknown);
                Assert.IsType<PgKeyspaceMissingException>(failure.InnerException);
            },
            cancellationToken
        );
    }

    #endregion
}
