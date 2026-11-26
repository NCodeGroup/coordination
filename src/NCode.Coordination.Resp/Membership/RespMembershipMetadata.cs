// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace NCode.Coordination.Resp.Membership;

/// <summary>
/// Encodes the metadata of a member as one text value and decodes it back, so any text round-trips.
/// </summary>
/// <remarks>
/// Each entry is <c>&lt;name length&gt;:&lt;name&gt;&lt;value length&gt;:&lt;value&gt;</c>, in ordinal order of the
/// names. The lengths count UTF-16 code units, so no character in a name or a value needs escaping.
/// </remarks>
internal static class RespMembershipMetadata
{
    /// <summary>
    /// Encodes metadata as length-prefixed text.
    /// </summary>
    /// <param name="metadata">The metadata to encode.</param>
    /// <returns>The encoded text, which is empty when <paramref name="metadata"/> is empty.</returns>
    /// <exception cref="ArgumentNullException">The metadata is null.</exception>
    public static string Encode(ImmutableDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string[] names = [.. metadata.Keys];
        Array.Sort(names, StringComparer.Ordinal);

        var builder = new StringBuilder();
        foreach (var name in names)
        {
            AppendText(builder, name);
            AppendText(builder, metadata[name]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes length-prefixed text that <see cref="Encode"/> produced.
    /// </summary>
    /// <param name="text">The encoded text.</param>
    /// <param name="metadata">The metadata, when the text is well formed; otherwise null.</param>
    /// <returns>True when the text is well formed; false on any digit, colon or length that doesn't fit.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    public static bool TryDecode(
        string text,
        [NotNullWhen(true)] out ImmutableDictionary<string, string>? metadata
    )
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < text.Length)
        {
            if (
                !TryReadText(text, ref position, out var name)
                || !TryReadText(text, ref position, out var value)
                || !builder.TryAdd(name, value)
            )
            {
                metadata = null;
                return false;
            }
        }

        metadata = builder.ToImmutable();
        return true;
    }

    /// <summary>
    /// Appends a length, a colon and a text.
    /// </summary>
    /// <param name="builder">The builder to append to.</param>
    /// <param name="value">The text to append.</param>
    private static void AppendText(StringBuilder builder, string value)
    {
        var length = value.Length.ToString(CultureInfo.InvariantCulture);

        builder.Append(length).Append(':').Append(value);
    }

    /// <summary>
    /// Reads a length, a colon and that many characters.
    /// </summary>
    /// <param name="text">The encoded text.</param>
    /// <param name="position">The position to read from, moved past the text that was read.</param>
    /// <param name="value">The text that was read, or null when it is malformed.</param>
    /// <returns>True when a whole text was read; false when a digit, the colon or the length doesn't fit.</returns>
    private static bool TryReadText(
        string text,
        ref int position,
        [NotNullWhen(true)] out string? value
    )
    {
        value = null;

        var colon = text.IndexOf(':', position);
        if (colon <= position)
        {
            return false;
        }

        var digits = text.AsSpan(position, colon - position);
        foreach (var digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            return false;
        }

        var start = colon + 1;
        if (length > text.Length - start)
        {
            return false;
        }

        value = text.Substring(start, length);
        position = start + length;
        return true;
    }
}
