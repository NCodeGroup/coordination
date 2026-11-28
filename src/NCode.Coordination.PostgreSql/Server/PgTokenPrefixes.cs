// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The text each kind of token the PostgreSQL provider issues starts with.
/// </summary>
internal static class PgTokenPrefixes
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
