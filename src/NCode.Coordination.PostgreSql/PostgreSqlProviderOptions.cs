// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination;

/// <summary>
/// The settings of the PostgreSQL provider. Exactly one of <see cref="ConnectionString"/> and
/// <see cref="DataSource"/> must be set.
/// </summary>
public sealed class PostgreSqlProviderOptions
{
    /// <summary>
    /// Gets or sets an Npgsql connection string. When set, the provider builds its own data source from it, with
    /// <c>Enlist</c> off and, when the string leaves <c>Keep Alive</c> at 0, a keep-alive of 15 seconds, and disposes
    /// that data source when the provider is disposed. A string that names several hosts is used through a filter
    /// that reaches the primary only.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets a factory for the host's own <see cref="NpgsqlDataSource"/>. When set, the provider uses that
    /// data source, filtered to the primary when it names several hosts, and never disposes it.
    /// </summary>
    /// <remarks>
    /// The provider holds one connection for listening, which needs direct access to PostgreSQL or session pooling:
    /// transaction pooling does not keep a session's <c>LISTEN</c>. The data source must not use multiplexing, and
    /// the provider opens every connection outside any ambient transaction.
    /// </remarks>
    public Func<IServiceProvider, NpgsqlDataSource>? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the profile that declares the durability of the deployment, which the provider's startup probe
    /// checks as far as it can. It is <see cref="PostgreSqlProfiles.Asynchronous"/> by default.
    /// </summary>
    public PostgreSqlProfile Profile { get; set; } = PostgreSqlProfiles.Asynchronous;

    /// <summary>
    /// Gets or sets the name of the schema that holds the provider's tables: 1 to 63 lowercase letters, digits and
    /// underscores, starting with a letter or an underscore. It is <c>coordination</c> by default.
    /// </summary>
    public string Schema { get; set; } = PostgreSqlSchema.DefaultName;

    /// <summary>
    /// Gets or sets a value that says whether the provider applies the schema's migrations when it starts, under an
    /// advisory lock. It is true by default; a host that applies them itself with
    /// <see cref="PostgreSqlSchema.GetMigrations(string)"/> sets it to false.
    /// </summary>
    public bool ApplyMigrations { get; set; } = true;

    /// <summary>
    /// Gets or sets the longest that a statement may run on the server, set per operation. It is at least one
    /// millisecond and less than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the longest that a statement may wait for a lock, set per operation. It is at least one
    /// millisecond and less than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the longest that a session may sit idle inside a transaction, set per operation. It is at least
    /// one millisecond and less than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan IdleInTransactionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the time between the passes that delete expired rows. It is at least one millisecond and less
    /// than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the most rows that a sweep deletes in one statement. It is from 1 to 100,000, and 1,000 by
    /// default.
    /// </summary>
    public int SweepBatchSize { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets how long a change log keeps its entries, which bounds how long a watch may fall behind before it
    /// resynchronizes. It is from one second to one day, and 5 minutes by default.
    /// </summary>
    public TimeSpan LogRetention { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the time between the periodic reads of each change log without a wake-up. It is at least one
    /// millisecond and less than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(5);
}
