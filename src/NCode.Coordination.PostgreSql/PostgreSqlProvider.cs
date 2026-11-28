// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Claims;
using NCode.Coordination.PostgreSql.Counters;
using NCode.Coordination.PostgreSql.Elections;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Membership;
using NCode.Coordination.PostgreSql.Notifications;
using NCode.Coordination.PostgreSql.Occurrences;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Sequences;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sessions;
using NCode.Coordination.PostgreSql.Sets;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Sweep;
using NCode.Coordination.PostgreSql.Transactions;

namespace NCode.Coordination.PostgreSql;

/// <summary>
/// The provider that keeps every primitive in the tables of one PostgreSQL schema, through Npgsql.
/// </summary>
internal sealed class PostgreSqlProvider : ICoordinationProvider, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Returns the primitives that the provider serves on a server of a profile. The server keeps every one of them in
    /// tables and its clock measures every expiry. The provider can't see whether a failover keeps the writes it
    /// acknowledged, so it declares every primitive linearizable only until a failover, and the durability that the
    /// profile names. A transaction and a counter's batch run as one database transaction over the rows of every
    /// partition, so each is atomic across partitions. A sequence has no expiry. A record watch and a notification
    /// read a log that the server numbers and bounds, so each delivers at most once with gap detection: a gap that the
    /// log fills is never seen, and one that it can't is a resync.
    /// </summary>
    /// <param name="profile">The profile whose durability the provider declares.</param>
    /// <returns>The primitives: records, transactions, occurrences, sets, counters, sequences, sessions, membership, leases, semaphores, elections, claims, record watches and notifications.</returns>
    /// <exception cref="ArgumentNullException">The profile is null.</exception>
    internal static ImmutableArray<PrimitiveSupport> Declare(PostgreSqlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return
        [
            ServerClocked(Primitives.Record, profile),
            AtomicServerClocked(Primitives.Transaction, profile),
            ServerClocked(Primitives.Occurrence, profile),
            ServerClocked(Primitives.Set, profile),
            AtomicServerClocked(Primitives.Counter, profile),
            PrimitiveSupport.Supported(
                Primitives.Sequence,
                Guarantees.LinearizableUntilFailover,
                profile.Durability
            ),
            ServerClocked(Primitives.Session, profile),
            ServerClocked(Primitives.Membership, profile),
            ServerClocked(Primitives.Lease, profile),
            ServerClocked(Primitives.Semaphore, profile),
            ServerClocked(Primitives.Election, profile),
            ServerClocked(Primitives.Claim, profile),
            // A record watch and a notification read a log that the server numbers and bounds (ADR-1034 rule 9).
            PrimitiveSupport.Supported(
                Primitives.RecordWatch,
                Guarantees.LinearizableUntilFailover,
                Guarantees.AtMostOnceWithGapDetection
            ),
            PrimitiveSupport.Supported(
                Primitives.Notification,
                Guarantees.AtMostOnceWithGapDetection
            ),
        ];
    }

    /// <summary>
    /// Describes a primitive that the server keeps in tables and whose expiry the server's clock measures.
    /// </summary>
    /// <param name="primitive">The primitive.</param>
    /// <param name="profile">The profile whose durability the provider declares.</param>
    /// <returns>The support: linearizable until a failover, the profile's durability and the server's clock.</returns>
    private static PrimitiveSupport ServerClocked(Primitive primitive, PostgreSqlProfile profile) =>
        PrimitiveSupport.Supported(
            primitive,
            Guarantees.LinearizableUntilFailover,
            profile.Durability,
            Guarantees.ServerClock
        );

    /// <summary>
    /// Describes a primitive that the server keeps in tables, whose expiry the server's clock measures and whose
    /// changes are atomic across partitions.
    /// </summary>
    /// <param name="primitive">The primitive.</param>
    /// <param name="profile">The profile whose durability the provider declares.</param>
    /// <returns>The support: the guarantees of <see cref="ServerClocked"/> and atomicity across partitions.</returns>
    private static PrimitiveSupport AtomicServerClocked(
        Primitive primitive,
        PostgreSqlProfile profile
    ) =>
        PrimitiveSupport.Supported(
            primitive,
            Guarantees.LinearizableUntilFailover,
            profile.Durability,
            Guarantees.ServerClock,
            Guarantees.AtomicAcrossPartitions
        );

    /// <summary>
    /// Creates the provider and the services that share its data source.
    /// </summary>
    /// <param name="source">The source of the provider's connections. The provider disposes it, which disposes the data source only when the source owns it.</param>
    /// <param name="profile">The profile that the server must match.</param>
    /// <param name="settings">The settings of the provider.</param>
    /// <exception cref="ArgumentNullException">The source, the profile or the settings are null.</exception>
    public PostgreSqlProvider(PgSource source, PostgreSqlProfile profile, PgSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        Source = source;
        Profile = profile;
        Settings = settings;
        Server = new PgServer(source, settings);
        Tokens = new PgRandomTokens();
        Keyspaces = new PgKeyspaces(Server, settings.Schema, Tokens);
        RecordTable = new PgRecordTable(Server, Keyspaces, settings);
        Hub = new PgLogHub(source, Keyspaces, settings, TimeProvider.System);
        ChangeLog = new PgChangeLog(Server, settings.Schema);
        var declared = Declare(profile);
        Declaration = new ProviderDeclaration(
            PostgreSqlCoordinationBuilderExtensions.ProviderName,
            declared
        )
        {
            Profile = profile.Name,
        };
        var sweeps = new PgSweeps(Server, settings, RecordTable);
        Sweeper = new PgSweeper(sweeps.SweepAsync, settings.SweepInterval, TimeProvider.System);
        Services = CreateServices();
    }

    /// <summary>
    /// Gets the source of the provider's connections.
    /// </summary>
    private PgSource Source { get; }

    /// <summary>
    /// Gets the profile that the server must match.
    /// </summary>
    private PostgreSqlProfile Profile { get; }

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    private PgSettings Settings { get; }

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private PgServer Server { get; }

    /// <summary>
    /// Gets the source of the session ids, owner tokens and keyspace ids.
    /// </summary>
    private IPgTokens Tokens { get; }

    /// <summary>
    /// Gets the cache of the keyspaces' row ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; }

    /// <summary>
    /// Gets the reads and commits of the records that the record and transaction services and the sweep share.
    /// </summary>
    private PgRecordTable RecordTable { get; }

    /// <summary>
    /// Gets the listening connection and the fanouts of the logs that watches and notifications read.
    /// </summary>
    private PgLogHub Hub { get; }

    /// <summary>
    /// Gets the reads of the change log that the record watches read.
    /// </summary>
    private PgChangeLog ChangeLog { get; }

    /// <summary>
    /// Gets the loop that deletes the expired rows.
    /// </summary>
    private PgSweeper Sweeper { get; }

    /// <summary>
    /// Gets the primitive services by the interface that each implements.
    /// </summary>
    private FrozenDictionary<Type, object> Services { get; }

    /// <inheritdoc />
    public ProviderDeclaration Declaration { get; }

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return Services.GetValueOrDefault(serviceType);
    }

    /// <inheritdoc />
    public async ValueTask ProbeAsync(CancellationToken cancellationToken)
    {
        await new PgProbe(Server, Source, Profile, Settings)
            .RunAsync(cancellationToken)
            .ConfigureAwait(false);
        Sweeper.Start();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Sweeper.DisposeAsync().ConfigureAwait(false);
        await Hub.DisposeAsync().ConfigureAwait(false);
        await Source.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Sweeper.Dispose();
        Hub.Dispose();
        Source.Dispose();
    }

    /// <summary>
    /// Creates the primitive services over the provider's server, keyspaces, record table, change log and hub.
    /// </summary>
    /// <returns>The services by the interface that each implements.</returns>
    private FrozenDictionary<Type, object> CreateServices()
    {
        var schema = Settings.Schema;
        var grants = new PgGrants(RecordTable);

        var services = new Dictionary<Type, object>
        {
            [typeof(IRecordService)] = new PgRecordService(RecordTable),
            [typeof(ITransactionService)] = new PgTransactionService(RecordTable),
            [typeof(IOccurrenceService)] = new PgOccurrenceService(Server, Keyspaces, schema),
            [typeof(ISetService)] = new PgSetService(Server, Keyspaces, schema),
            [typeof(ICounterService)] = new PgCounterService(Server, Keyspaces, schema),
            [typeof(ISequenceService)] = new PgSequenceService(Server, Keyspaces, schema),
            [typeof(ISessionService)] = new PgSessionService(RecordTable, Tokens),
            [typeof(IMembershipService)] = new PgMembershipService(RecordTable, ChangeLog, Hub),
            [typeof(ILeaseService)] = new PgLeaseService(grants, Tokens),
            [typeof(ISemaphoreService)] = new PgSemaphoreService(grants, Tokens),
            [typeof(IElectionService)] = new PgElectionService(grants, Tokens),
            [typeof(IClaimService)] = new PgClaimService(Server, Keyspaces, schema),
            [typeof(IRecordWatchService)] = new PgRecordWatchService(Keyspaces, ChangeLog, Hub),
            [typeof(INotificationService)] = new PgNotificationService(
                Server,
                Keyspaces,
                Settings,
                Hub
            ),
        };

        return services.ToFrozenDictionary();
    }
}
