// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The vendor profiles that the RESP provider supports.
/// </summary>
public static class RespProfiles
{
    /// <summary>
    /// Gets the oldest Valkey version the provider accepts.
    /// </summary>
    private static Version ValkeyMinimumVersion { get; } = new(7, 2, 0);

    /// <summary>
    /// Gets the oldest Garnet version the provider accepts.
    /// </summary>
    private static Version GarnetMinimumVersion { get; } = new(2, 2, 13);

    /// <summary>
    /// Gets the profile for Valkey 7.2.0 and later.
    /// </summary>
    /// <remarks>
    /// Valkey carries channel messages on sharded Pub/Sub channels.
    /// </remarks>
    public static RespProfile Valkey { get; } =
        new("valkey", "valkey", "valkey_version", ValkeyMinimumVersion, shardedChannels: true);

    // Garnet's settings for Lua: ADR-1023 rule 9.
    /// <summary>
    /// Gets the profile for Garnet 2.2.13 and later.
    /// </summary>
    /// <remarks>
    /// Garnet carries channel messages on plain channels. It must run with <c>--lua --lua-transaction-mode</c> so that
    /// a script runs as one transaction.
    /// </remarks>
    public static RespProfile Garnet { get; } =
        new("garnet", "garnet", "garnet_version", GarnetMinimumVersion, shardedChannels: false);
}
