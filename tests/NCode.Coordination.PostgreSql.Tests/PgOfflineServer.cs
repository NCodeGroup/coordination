// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// A source, server, keyspaces and record table aimed at a port nothing listens on, for unit tests of what an
/// operation checks before it reaches a server.
/// </summary>
internal sealed class PgOfflineServer : IAsyncDisposable
{
    /// <summary>
    /// The connection string of a server that nothing listens on.
    /// </summary>
    public const string ConnectionString =
        "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1";

    /// <summary>
    /// Initializes a new instance of the <see cref="PgOfflineServer"/> class.
    /// </summary>
    public PgOfflineServer()
    {
        Settings = PgSettings.Default;
        Source = PgSource.FromConnectionString(ConnectionString);
        Server = new PgServer(Source, Settings);
        Keyspaces = new PgKeyspaces(Server, Settings.Schema);
        Table = new PgRecordTable(Server, Keyspaces, Settings);
    }

    /// <summary>
    /// Gets the default settings of the provider.
    /// </summary>
    public PgSettings Settings { get; }

    /// <summary>
    /// Gets the source of connections to the unreachable server.
    /// </summary>
    public PgSource Source { get; }

    /// <summary>
    /// Gets the server over the source.
    /// </summary>
    public PgServer Server { get; }

    /// <summary>
    /// Gets the cache of keyspace ids over the server.
    /// </summary>
    public PgKeyspaces Keyspaces { get; }

    /// <summary>
    /// Gets the record table over the server.
    /// </summary>
    public PgRecordTable Table { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
