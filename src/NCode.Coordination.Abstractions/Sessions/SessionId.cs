// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// The identity of a session: its keyspace and the provider's token for it.
/// </summary>
/// <remarks>
/// Two session ids are equal when their keyspaces and tokens are equal.
/// </remarks>
public sealed record SessionId
{
    /// <summary>
    /// Creates a session id.
    /// </summary>
    /// <param name="keyspace">The keyspace the session lives in.</param>
    /// <param name="value">The provider's token for the session: 1 to 128 visible ASCII characters.</param>
    /// <exception cref="ArgumentNullException">The keyspace or the value is null.</exception>
    /// <exception cref="ArgumentException">The value is not a valid token.</exception>
    public SessionId(Keyspace keyspace, string value)
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        Keyspace = keyspace;
        Value = OpaqueToken.Validate(value, CoordinationLimits.MaxTokenLength, nameof(value));
    }

    /// <summary>
    /// Gets the keyspace the session lives in.
    /// </summary>
    public Keyspace Keyspace { get; }

    /// <summary>
    /// Gets the provider's token for the session.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the keyspace and the token, joined by '@'.
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Keyspace}@{Value}";
}
