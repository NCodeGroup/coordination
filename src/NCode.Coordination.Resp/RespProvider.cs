// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Counters;
using NCode.Coordination.Resp.Occurrences;
using NCode.Coordination.Resp.Sequences;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Sets;
using StackExchange.Redis;

namespace NCode.Coordination.Resp;

/// <summary>
/// The provider that keeps every primitive on a RESP server, Valkey or Garnet, through StackExchange.Redis.
/// </summary>
internal sealed class RespProvider : ICoordinationProvider, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the primitives that the provider serves, in the order of <see cref="Primitives.All"/>. The server keeps
    /// every one of them in memory and its clock measures every expiry. The provider can't see whether the server
    /// persists or replicates its writes, so it declares them volatile and linearizable only until a failover. A
    /// counter's batch runs as one script on one slot, so it is atomic across partitions. A sequence has no expiry.
    /// </summary>
    private static PrimitiveSupport[] SupportedPrimitives { get; } =
    [
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
    ];

    /// <summary>
    /// Creates the exception of a probe that found the server unsupported.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The exception, which says the call did not apply.</returns>
    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    /// <summary>
    /// Creates the provider and the services that share its connection.
    /// </summary>
    /// <param name="connection">The holder of the provider's connection to its server. The provider disposes the holder, which disposes the multiplexer only when it owns it.</param>
    /// <param name="profile">The vendor profile that the server must match.</param>
    /// <exception cref="ArgumentNullException">The connection or the profile is null.</exception>
    public RespProvider(RespConnection connection, RespProfile profile)
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
        Services = new Dictionary<Type, object>
        {
            [typeof(IOccurrenceService)] = new RespOccurrenceService(Server),
            [typeof(ISetService)] = new RespSetService(Server),
            [typeof(ICounterService)] = new RespCounterService(Server),
            [typeof(ISequenceService)] = new RespSequenceService(Server),
        };
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
    private Dictionary<Type, object> Services { get; }

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
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => Connection.Dispose();
}
