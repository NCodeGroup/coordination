// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp;

/// <summary>
/// The provider that keeps every primitive on a RESP server, Valkey or Garnet, through StackExchange.Redis.
/// </summary>
/// <param name="connection">The holder of the provider's connection to its server. The provider disposes the holder, which disposes the multiplexer only when it owns it.</param>
/// <param name="profile">The vendor profile that the server must match.</param>
/// <exception cref="ArgumentNullException">The connection or the profile is null.</exception>
internal sealed class RespProvider(RespConnection connection, RespProfile profile)
    : ICoordinationProvider,
        IAsyncDisposable,
        IDisposable
{
    /// <summary>
    /// Creates the exception of a probe that found the server unsupported.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The exception, which says the call did not apply.</returns>
    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    /// <summary>
    /// Gets the connection of the provider to its server.
    /// </summary>
    private RespConnection Connection { get; } =
        connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = new(connection);

    /// <summary>
    /// Gets the vendor profile that the server must match.
    /// </summary>
    private RespProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <inheritdoc />
    public ProviderDeclaration Declaration { get; } =
        new(RespCoordinationBuilderExtensions.ProviderName) { Profile = profile.Name };

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return null;
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
