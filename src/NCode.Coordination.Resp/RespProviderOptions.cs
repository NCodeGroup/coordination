// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination;

/// <summary>
/// The settings of the RESP provider. Exactly one of <see cref="Configuration"/> and <see cref="Connection"/> must be
/// set.
/// </summary>
public sealed class RespProviderOptions
{
    /// <summary>
    /// Gets or sets a StackExchange.Redis configuration string. When set, the provider opens its own connection from
    /// it, and disposes that connection when the provider is disposed.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Gets or sets a factory for the host's own <see cref="IConnectionMultiplexer"/>. When set, the provider uses
    /// that connection and never disposes it.
    /// </summary>
    public Func<IServiceProvider, IConnectionMultiplexer>? Connection { get; set; }

    /// <summary>
    /// Gets or sets the profile that names the vendor of the server, which the provider's startup probe checks. It is
    /// <see cref="RespProfiles.Valkey"/> by default.
    /// </summary>
    public RespProfile Profile { get; set; } = RespProfiles.Valkey;
}
