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
    /// log fills is never seen, and one that it can't is a resync (ADR-1034 rule 9).
    /// </summary>
    /// <param name="profile">The profile whose durability the provider declares.</param>
    /// <returns>The primitives: records, transactions, occurrences, sets, counters, sequences, sessions, membership, leases, semaphores, elections, claims, record watches and notifications.</returns>
    /// <exception cref="ArgumentNullException">The profile is null.</exception>
    internal static ImmutableArray<PrimitiveSupport> Declare(PostgreSqlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return
        [
            PrimitiveSupport.Supported(
                Primitives.Record,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Transaction,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock,
                Guarantees.AtomicAcrossPartitions
            ),
            PrimitiveSupport.Supported(
                Primitives.Occurrence,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Set,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Counter,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock,
                Guarantees.AtomicAcrossPartitions
            ),
            PrimitiveSupport.Supported(
                Primitives.Sequence,
                Guarantees.LinearizableUntilFailover,
                profile.Durability
            ),
            PrimitiveSupport.Supported(
                Primitives.Session,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Membership,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Lease,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Semaphore,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Election,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
            PrimitiveSupport.Supported(
                Primitives.Claim,
                Guarantees.LinearizableUntilFailover,
                profile.Durability,
                Guarantees.ServerClock
            ),
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
        Keyspaces = new PgKeyspaces(Server, settings.Schema);
        RecordTable = new PgRecordTable(Server, Keyspaces, settings);
        Hub = new PgLogHub(source, Keyspaces, settings, TimeProvider.System);
        ChangeLog = new PgChangeLog(Server, settings.Schema);
        Declaration = new ProviderDeclaration(
            PostgreSqlCoordinationBuilderExtensions.ProviderName,
            Declare(profile)
        )
        {
            Profile = profile.Name,
        };
        Sweeper = new PgSweeper(
            new PgSweeps(Server, settings, RecordTable),
            settings.SweepInterval,
            TimeProvider.System
        );
        var tokens = new PgRandomTokens();
        var grants = new PgGrants(RecordTable);
        Services = new Dictionary<Type, object>
        {
            [typeof(IRecordService)] = new PgRecordService(RecordTable),
            [typeof(ITransactionService)] = new PgTransactionService(RecordTable),
            [typeof(IOccurrenceService)] = new PgOccurrenceService(
                Server,
                Keyspaces,
                settings.Schema
            ),
            [typeof(ISetService)] = new PgSetService(Server, Keyspaces, settings.Schema),
            [typeof(ICounterService)] = new PgCounterService(Server, Keyspaces, settings.Schema),
            [typeof(ISequenceService)] = new PgSequenceService(Server, Keyspaces, settings.Schema),
            [typeof(ISessionService)] = new PgSessionService(RecordTable, tokens),
            [typeof(IMembershipService)] = new PgMembershipService(RecordTable),
            [typeof(ILeaseService)] = new PgLeaseService(grants, tokens),
            [typeof(ISemaphoreService)] = new PgSemaphoreService(grants, tokens),
            [typeof(IElectionService)] = new PgElectionService(grants, tokens),
            [typeof(IClaimService)] = new PgClaimService(Server, Keyspaces, settings.Schema),
            [typeof(IRecordWatchService)] = new PgRecordWatchService(Keyspaces, ChangeLog, Hub),
            [typeof(INotificationService)] = new PgNotificationService(
                Server,
                Keyspaces,
                settings,
                Hub
            ),
        }.ToFrozenDictionary();
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
}
