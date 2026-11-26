// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The vendor profiles that the RESP provider supports.
/// </summary>
public static class RespProfiles
{
    /// <summary>
    /// Gets the profile for Valkey 7.2.0 and later.
    /// </summary>
    /// <remarks>
    /// Valkey carries channel messages on sharded Pub/Sub channels.
    /// </remarks>
    public static RespProfile Valkey { get; } =
        new("valkey", "valkey", "valkey_version", new Version(7, 2, 0), shardedChannels: true);

    /// <summary>
    /// Gets the profile for Garnet 2.2.13 and later.
    /// </summary>
    /// <remarks>
    /// Garnet carries channel messages on plain channels. It must run with <c>--lua --lua-transaction-mode</c> so that
    /// a script runs as one transaction (ADR-1023 rule 9).
    /// </remarks>
    public static RespProfile Garnet { get; } =
        new("garnet", "garnet", "garnet_version", new Version(2, 2, 13), shardedChannels: false);
}
