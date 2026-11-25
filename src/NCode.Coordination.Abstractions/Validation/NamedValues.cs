// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Validation;

/// <summary>
/// The one rule for named text values: extension settings and metadata, keyed by vocabulary names.
/// </summary>
internal static class NamedValues
{
    /// <summary>
    /// Checks that named values are within their limits and that every name is a vocabulary name.
    /// </summary>
    /// <param name="values">The values to check.</param>
    /// <param name="maxCount">The largest number of entries.</param>
    /// <param name="maxValueLength">The longest value, in characters.</param>
    /// <param name="paramName">The name of the parameter or property that holds the values.</param>
    /// <returns>The values, comparing names and values ordinally.</returns>
    public static ImmutableDictionary<string, string> Check(
        ImmutableDictionary<string, string> values,
        int maxCount,
        int maxValueLength,
        string paramName
    )
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        if (values.Count > maxCount)
        {
            throw new ArgumentException(
                $"At most {maxCount} entries are allowed, and there are {values.Count}.",
                paramName
            );
        }

        foreach (var (key, value) in values)
        {
            VocabularyName.Validate(key, paramName);

            if (value is null)
            {
                throw new ArgumentException($"The value of '{key}' is null.", paramName);
            }

            if (value.Length > maxValueLength)
            {
                throw new ArgumentException(
                    $"The value of '{key}' is at most {maxValueLength} characters, and it has {value.Length}.",
                    paramName
                );
            }
        }

        return values.WithComparers(StringComparer.Ordinal, StringComparer.Ordinal);
    }
}
