// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Validation;

/// <summary>
/// The one rule for opaque tokens: owner tokens, session tokens, node ids and continuation tokens.
/// </summary>
internal static class OpaqueToken
{
    /// <summary>
    /// Checks that a value is a token: 1 to a maximum number of visible ASCII characters, from '!' to '~'.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="maxLength">The longest token, in characters.</param>
    /// <param name="paramName">The name of the parameter or property that holds the value.</param>
    /// <returns>The value, when it is a token.</returns>
    public static string Validate(string value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Length == 0 || value.Length > maxLength)
        {
            throw new ArgumentException(
                $"A token has 1 to {maxLength} characters, and this one has {value.Length}.",
                paramName
            );
        }

        foreach (var character in value)
        {
            if (character is < '!' or > '~')
            {
                throw new ArgumentException(
                    "A token holds only visible ASCII characters, from '!' to '~'.",
                    paramName
                );
            }
        }

        return value;
    }
}
