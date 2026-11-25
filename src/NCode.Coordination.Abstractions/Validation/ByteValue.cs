// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Validation;

/// <summary>
/// The one rule for byte values a type keeps: a length limit and a copy of its own.
/// </summary>
internal static class ByteValue
{
    /// <summary>
    /// Checks the length of a value and copies it, so the result never shares the caller's buffer.
    /// </summary>
    /// <param name="value">The value to check and copy.</param>
    /// <param name="maxLength">The longest value, in bytes.</param>
    /// <param name="paramName">The name of the parameter or property that holds the value.</param>
    /// <returns>The copy of the value, or an empty value when it is empty.</returns>
    public static ReadOnlyMemory<byte> Copy(
        ReadOnlyMemory<byte> value,
        int maxLength,
        string paramName
    )
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"A value has at most {maxLength} bytes.", paramName);
        }

        if (value.IsEmpty)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return value.ToArray();
    }
}
