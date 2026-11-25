// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// The token a grant returns, which renews and releases only that grant.
/// </summary>
/// <remarks>
/// It identifies a holder and is not a credential. It is 1 to <see cref="CoordinationLimits.MaxTokenLength"/> visible
/// ASCII characters, and two tokens are equal when their text is equal.
/// </remarks>
public sealed record OwnerToken
{
    /// <summary>
    /// Creates an owner token.
    /// </summary>
    /// <param name="value">The token's text: 1 to 128 visible ASCII characters, from '!' to '~'.</param>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">The value is not a valid token.</exception>
    public OwnerToken(string value)
    {
        Value = OpaqueToken.Validate(value, CoordinationLimits.MaxTokenLength, nameof(value));
    }

    /// <summary>
    /// Gets the token's text.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the token's text.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => Value;
}
