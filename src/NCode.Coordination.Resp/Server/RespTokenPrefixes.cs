// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server;

// ADR-1028 rules 2 and 3.
/// <summary>
/// The text each kind of token the RESP provider issues starts with.
/// </summary>
internal static class RespTokenPrefixes
{
    /// <summary>
    /// The text a session id starts with.
    /// </summary>
    public const string Session = "s-";

    /// <summary>
    /// The text an owner token starts with.
    /// </summary>
    public const string Owner = "o-";
}
