// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
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
    /// <remarks>
    /// The provider runs its scripts and commands on the connection's default database, with the connection's own
    /// settings. It reads replies in RESP2 and RESP3, whichever the connection negotiates, and works with any backlog
    /// policy: a call whose wait for a reply times out, in the backlog or after it, is reported as possibly applied.
    /// The connection's command map must keep <c>EVAL</c> and <c>EVALSHA</c>; the provider's startup check fails the
    /// host's start otherwise.
    /// </remarks>
    public Func<IServiceProvider, IConnectionMultiplexer>? Connection { get; set; }

    /// <summary>
    /// Gets or sets the profile that names the vendor of the server, which the provider's startup probe checks. It is
    /// <see cref="RespProfiles.Valkey"/> by default.
    /// </summary>
    public RespProfile Profile { get; set; } = RespProfiles.Valkey;

    /// <summary>
    /// Gets or sets the time between two periodic checks of each notification subscription and watch, which reads its
    /// channel's number and, for a watch, removes and announces the keyspace's expired state. It is more than zero and
    /// less than one hour, and 5 seconds by default.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = RespSubscriptions.DefaultCheckInterval;
}
