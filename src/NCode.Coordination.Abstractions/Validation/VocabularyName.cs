// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace NCode.Coordination.Validation;

/// <summary>
/// The one rule for the names of guarantees, dimensions, primitives, providers, profiles and features.
/// </summary>
internal static partial class VocabularyName
{
    /// <summary>
    /// The longest name, in characters.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Checks that a value is a name: lowercase letters and digits, with a single '.' or '-' between them.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name of the parameter or property that holds the value.</param>
    /// <returns>The value, when it is a name.</returns>
    public static string Validate(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A name is at most {MaxLength} characters, and this one has {value.Length}.",
                paramName
            );
        }

        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a name: use lowercase letters and digits, with a single '.' or '-' between them.",
                paramName
            );
        }

        return value;
    }

    /// <summary>Matches a name: lowercase letters and digits, with a single '.' or '-' between them.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
