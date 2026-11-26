// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp;

/// <summary>
/// The provider that keeps every primitive on a RESP server, Valkey or Garnet, through StackExchange.Redis.
/// </summary>
internal sealed class RespProvider : ICoordinationProvider, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Creates the exception of a probe that found the server unsupported.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The exception, which says the call did not apply.</returns>
    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="connection">The connection of the provider to its server, which the provider disposes.</param>
    /// <param name="profile">The vendor profile that the server must match.</param>
    /// <exception cref="ArgumentNullException">The connection or the profile is null.</exception>
    public RespProvider(RespConnection connection, RespProfile profile)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);

        Connection = connection;
        Server = new RespServer(connection);
        Profile = profile;
        Declaration = new ProviderDeclaration(RespCoordinationBuilderExtensions.ProviderName)
        {
            Profile = profile.Name,
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

    /// <inheritdoc />
    public ProviderDeclaration Declaration { get; }

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

        string? text;
        try
        {
            text = (string?)reply;
        }
        catch (InvalidCastException)
        {
            text = null;
        }

        if (text is null)
        {
            throw Unsupported(
                "The RESP server answered the probe script with an unexpected reply."
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
