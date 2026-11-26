// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// The names of the server commands the provider sends.
/// </summary>
internal static class RespCommands
{
    /// <summary>The command that deletes a key.</summary>
    public const string Delete = "DEL";

    /// <summary>The command that reads the string value of a key.</summary>
    public const string Get = "GET";

    /// <summary>The command that adds one to the integer value of a key, creating it at 0 when it is missing.</summary>
    public const string Increment = "INCR";

    /// <summary>The command that adds an amount to the integer value of a key, creating it at 0 when it is missing.</summary>
    public const string IncrementBy = "INCRBY";

    /// <summary>The command that writes the string value of a key, with options for its condition and expiry.</summary>
    public const string Set = "SET";
}
