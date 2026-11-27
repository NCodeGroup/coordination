// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;
using NCode.Coordination.Resp.Claims;
using NCode.Coordination.Resp.Counters;
using NCode.Coordination.Resp.Elections;
using NCode.Coordination.Resp.Leases;
using NCode.Coordination.Resp.Membership;
using NCode.Coordination.Resp.Notifications;
using NCode.Coordination.Resp.Occurrences;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Sequences;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Sessions;
using NCode.Coordination.Resp.Sets;
using NCode.Coordination.Resp.Transactions;
using StackExchange.Redis;

namespace NCode.Coordination.Resp;

/// <summary>
/// The provider that keeps every primitive on a RESP server, Valkey or Garnet, through StackExchange.Redis.
/// </summary>
internal sealed class RespProvider : ICoordinationProvider, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the primitives that the provider serves. The server keeps
    /// every one of them in memory and its clock measures every expiry. The provider can't see whether the server
    /// persists or replicates its writes, so it declares them volatile and linearizable only until a failover. A
    /// counter's batch runs as one script on one slot, so it is atomic across partitions, and a transaction, like a
    /// counter's batch, runs as one script on one slot. A sequence has no expiry. A record watch and a notification
    /// subscription deliver each change at most once, numbered so that a gap shows, and resync on any doubt.
    /// </summary>
    private static ImmutableArray<PrimitiveSupport> SupportedPrimitives { get; } =
    [
        PrimitiveSupport.Supported(
            Primitives.Record,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.RecordWatch,
            Guarantees.LinearizableUntilFailover,
            Guarantees.AtMostOnceWithGapDetection
        ),
        PrimitiveSupport.Supported(
            Primitives.Transaction,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock,
            Guarantees.AtomicAcrossPartitions
        ),
        PrimitiveSupport.Supported(
            Primitives.Occurrence,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Set,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Counter,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock,
            Guarantees.AtomicAcrossPartitions
        ),
        PrimitiveSupport.Supported(
            Primitives.Sequence,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile
        ),
        PrimitiveSupport.Supported(
            Primitives.Session,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Membership,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Lease,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Semaphore,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Election,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(
            Primitives.Claim,
            Guarantees.LinearizableUntilFailover,
            Guarantees.Volatile,
            Guarantees.ServerClock
        ),
        PrimitiveSupport.Supported(Primitives.Notification, Guarantees.AtMostOnceWithGapDetection),
    ];

    /// <summary>
    /// Creates the exception of a probe that found the server unsupported.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The exception, which says the call did not apply.</returns>
    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    /// <summary>
    /// Returns every command the provider sends to a server of a profile, which the startup probe checks that the
    /// server's user may run: the scripting commands, the commands its operations send alone and the commands its
    /// scripts call, with the publish command of the profile's channels and the commands that subscribe to and
    /// unsubscribe from the profile's channels.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The command names, in upper case.</returns>
    /// <exception cref="ArgumentNullException">The profile is null.</exception>
    internal static RedisValue[] ProbedCommands(RespProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var publish = profile.ShardedChannels ? "SPUBLISH" : "PUBLISH";
        var subscribe = profile.ShardedChannels ? "SSUBSCRIBE" : "SUBSCRIBE";
        var unsubscribe = profile.ShardedChannels ? "SUNSUBSCRIBE" : "UNSUBSCRIBE";
        return
        [
            "EVAL",
            "EVALSHA",
            "SCRIPT",
            RespCommands.Delete,
            RespCommands.Get,
            "HDEL",
            "HGET",
            "HINCRBY",
            "HMGET",
            "HSET",
            RespCommands.Increment,
            RespCommands.IncrementBy,
            "PERSIST",
            "PEXPIRE",
            "PTTL",
            publish,
            RespCommands.Set,
            subscribe,
            "TIME",
            unsubscribe,
            "ZADD",
            "ZCARD",
            "ZCOUNT",
            "ZRANGE",
            "ZRANGEBYLEX",
            "ZRANGEBYSCORE",
            "ZREM",
            "ZREMRANGEBYSCORE",
            "ZSCORE",
        ];
    }

    /// <summary>
    /// Creates the provider and the services that share its connection.
    /// </summary>
    /// <param name="connection">The holder of the provider's connection to its server. The provider disposes the holder, which disposes the multiplexer only when it owns it.</param>
    /// <param name="profile">The vendor profile that the server must match.</param>
    /// <exception cref="ArgumentNullException">The connection or the profile is null.</exception>
    public RespProvider(RespConnection connection, RespProfile profile)
        : this(connection, profile, RespSubscriptions.DefaultCheckInterval) { }

    /// <summary>
    /// Creates the provider and the services that share its connection.
    /// </summary>
    /// <param name="connection">The holder of the provider's connection to its server. The provider disposes the holder, which disposes the multiplexer only when it owns it.</param>
    /// <param name="profile">The vendor profile that the server must match.</param>
    /// <param name="checkInterval">The time between two periodic checks of each notification subscription and watch.</param>
    /// <exception cref="ArgumentNullException">The connection or the profile is null.</exception>
    public RespProvider(RespConnection connection, RespProfile profile, TimeSpan checkInterval)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);

        Connection = connection;
        Profile = profile;
        Server = new RespServer(connection);
        Declaration = new ProviderDeclaration(
            RespCoordinationBuilderExtensions.ProviderName,
            SupportedPrimitives
        )
        {
            Profile = profile.Name,
        };
        var records = new RespRecordTable(Server, profile);
        var tokens = new RespRandomTokens();
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, profile, schedule);
        var recordService = new RespRecordService(records);
        var services = new Dictionary<Type, object>
        {
            [typeof(IRecordService)] = recordService,
            [typeof(IRecordWatchService)] = new RespRecordWatchService(
                records,
                recordService,
                subscriptions
            ),
            [typeof(ITransactionService)] = new RespTransactionService(records),
            [typeof(IOccurrenceService)] = new RespOccurrenceService(Server),
            [typeof(ISetService)] = new RespSetService(Server),
            [typeof(ICounterService)] = new RespCounterService(Server),
            [typeof(ISequenceService)] = new RespSequenceService(Server),
            [typeof(ISessionService)] = new RespSessionService(records, tokens),
            [typeof(IMembershipService)] = new RespMembershipService(records, subscriptions),
            [typeof(ILeaseService)] = new RespLeaseService(records, tokens),
            [typeof(ISemaphoreService)] = new RespSemaphoreService(records, tokens),
            [typeof(IElectionService)] = new RespElectionService(records, tokens),
            [typeof(IClaimService)] = new RespClaimService(Server),
            [typeof(INotificationService)] = new RespNotificationService(
                Server,
                profile,
                subscriptions
            ),
        };
        Services = services.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the connection of the provider to its server.
    /// </summary>
    private RespConnection Connection { get; }

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; }

    /// <summary>
    /// Gets the vendor profile that the server must match.
    /// </summary>
    private RespProfile Profile { get; }

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
        cancellationToken.ThrowIfCancellationRequested();

        RedisResult reply;
        try
        {
            reply = await Server
                .EvaluateAsync(RespScript.Probe, [], [], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CoordinationException e)
            when (e.InnerException is RedisServerException inner
                && !RespFailures.IsKnownReply(inner)
            )
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                false,
                $"The RESP server refused the provider's probe script: {inner.Message}. The provider runs Lua scripts; Garnet runs them only when started with --lua --lua-transaction-mode.",
                inner
            );
        }

        var type = reply.IsNull ? "null" : reply.Resp2Type.ToString();
        var text = reply.Resp2Type is ResultType.BulkString or ResultType.SimpleString
            ? (string?)reply
            : null;
        if (text is null)
        {
            throw Unsupported(
                $"The RESP server answered the probe script with a reply of type {type}, not text."
            );
        }

        var info = RespServer.ParseInfo(text);

        info.TryGetValue("server_name", out var name);
        if (!string.Equals(name, Profile.ServerName, StringComparison.Ordinal))
        {
            throw Unsupported(
                $"The RESP server is '{name ?? "unknown"}', but the provider uses the '{Profile.Name}' profile."
            );
        }

        info.TryGetValue(Profile.VersionField, out var value);
        if (
            value is null
            || !Version.TryParse(value, out var version)
            || version < Profile.MinimumVersion
        )
        {
            throw Unsupported(
                $"The '{Profile.Name}' profile needs {Profile.VersionField} {Profile.MinimumVersion} or later, and the server reports '{value ?? "none"}'."
            );
        }

        var commands = ProbedCommands(Profile);
        RedisResult permissions;
        try
        {
            permissions = await Server
                .EvaluateAsync(RespScript.ProbeCommands, [], commands, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CoordinationException e)
            when (e.InnerException is RedisServerException inner
                && !RespFailures.IsKnownReply(inner)
            )
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                false,
                $"The RESP server refused the provider's permission check: {inner.Message}.",
                inner
            );
        }

        var permissionsType = permissions.IsNull ? "null" : permissions.Resp2Type.ToString();
        if (permissions.IsNull || permissions.Resp2Type is not ResultType.Array)
        {
            throw Unsupported(
                $"The RESP server answered the permission check with a reply of type {permissionsType}, not a list."
            );
        }

        var items = (RedisResult[]?)permissions ?? [];
        var denied = new List<string>();
        foreach (var item in items)
        {
            var command = (string?)item;
            if (command is not null)
            {
                denied.Add(command);
            }
        }

        if (denied.Count > 0)
        {
            var list = string.Join(", ", denied);
            throw new CoordinationException(
                CoordinationErrorCodes.AccessDenied,
                false,
                $"The RESP server's user may not run {list}, which the provider sends."
            );
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => Connection.Dispose();
}
