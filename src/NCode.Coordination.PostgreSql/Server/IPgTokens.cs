// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Supplies the opaque tokens the PostgreSQL provider issues as session ids and owner tokens, so no clock is read and
/// tests can substitute them.
/// </summary>
internal interface IPgTokens
{
    /// <summary>
    /// Issues an opaque token: the prefix followed by 128 random bits.
    /// </summary>
    /// <param name="prefix">The text the token starts with.</param>
    /// <returns>The prefix followed by 32 lowercase hexadecimal digits.</returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    string Next(string prefix);

    /// <summary>
    /// Draws a random keyspace id that is not zero.
    /// </summary>
    /// <returns>The id.</returns>
    long NextKeyspaceId();
}
