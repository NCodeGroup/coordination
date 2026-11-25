// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a session that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Open"/>, <see cref="KeepAlive"/> or <see cref="Close"/>.</param>
internal sealed record SessionCall(string Operation)
{
    /// <summary>
    /// The operation that opens the session.
    /// </summary>
    public const string Open = "open";

    /// <summary>
    /// The operation that renews the session.
    /// </summary>
    public const string KeepAlive = "keep-alive";

    /// <summary>
    /// The operation that closes the session.
    /// </summary>
    public const string Close = "close";
}
