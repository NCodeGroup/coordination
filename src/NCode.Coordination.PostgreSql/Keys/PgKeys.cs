// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace NCode.Coordination.PostgreSql.Keys;

/// <summary>
/// Encodes keyspaces and keys as the names and texts that the provider's tables store.
/// </summary>
internal static class PgKeys
{
    /// <summary>
    /// Gets the UTF-8 encoding that throws on bytes that are not well-formed UTF-8, which
    /// <see cref="TryUnescape"/> uses.
    /// </summary>
    private static UTF8Encoding StrictUtf8 { get; } =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes one segment as a letter for its kind and its value: "t" and the escaped text, "n" and the invariant
    /// decimal, or "u" and the 32 lowercase hex digits of the UUID.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The encoded segment.</returns>
    /// <exception cref="ArgumentNullException">The segment is null.</exception>
    /// <exception cref="ArgumentException">The segment holds no value for its kind.</exception>
    public static string EncodeSegment(KeySegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return segment switch
        {
            { Kind: KeySegmentKind.Text, Text: { } text } => "t" + Escape(text),
            { Kind: KeySegmentKind.Number, Number: { } number } => "n"
                + number.ToString(CultureInfo.InvariantCulture),
            { Kind: KeySegmentKind.Uuid, Uuid: { } uuid } => "u"
                + uuid.ToString("N", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException(
                "The segment holds no value for its kind.",
                nameof(segment)
            ),
        };
    }

    /// <summary>
    /// Decodes one segment that <see cref="EncodeSegment"/> wrote: the inverse of <see cref="EncodeSegment"/>.
    /// </summary>
    /// <param name="text">The encoded segment.</param>
    /// <param name="segment">The segment, when the text is one that <see cref="EncodeSegment"/> writes.</param>
    /// <returns>
    /// False for an empty text, an unknown first letter, a '%' not followed by two uppercase hex digits, a raw
    /// character that escaping would have encoded, bytes that are not UTF-8, a number that does not round-trip, or a
    /// UUID that is not 32 lowercase hex digits; true otherwise.
    /// </returns>
    public static bool TryDecodeSegment(string? text, [NotNullWhen(true)] out KeySegment? segment)
    {
        segment = null;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var rest = text[1..];

        switch (text[0])
        {
            case 't':
                if (!TryUnescape(rest, out var unescaped))
                {
                    return false;
                }

                if (unescaped.Length == 0 || unescaped.Length > KeySegment.MaxTextLength)
                {
                    return false;
                }

                KeySegment textSegment;

                try
                {
                    textSegment = KeySegment.FromString(unescaped);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                var canonical = EncodeSegment(textSegment);

                if (!string.Equals(canonical, text, StringComparison.Ordinal))
                {
                    return false;
                }

                segment = textSegment;
                return true;

            case 'n':
                if (
                    !long.TryParse(
                        rest,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var number
                    )
                )
                {
                    return false;
                }

                var numberText = number.ToString(CultureInfo.InvariantCulture);

                if (!string.Equals(numberText, rest, StringComparison.Ordinal))
                {
                    return false;
                }

                segment = KeySegment.FromInt64(number);
                return true;

            case 'u':
                if (!Guid.TryParseExact(rest, "N", out var uuid))
                {
                    return false;
                }

                var uuidText = uuid.ToString("N", CultureInfo.InvariantCulture);

                if (!string.Equals(uuidText, rest, StringComparison.Ordinal))
                {
                    return false;
                }

                segment = KeySegment.FromGuid(uuid);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the name of a keyspace: its cluster, then a "/" and the encoded form of each segment.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The name, such as <c>prod/tcell-1/n42</c>.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public static string KeyspaceName(Keyspace keyspace)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        var builder = new StringBuilder(keyspace.Cluster);

        foreach (var segment in keyspace.Segments)
        {
            var encoded = EncodeSegment(segment);
            builder.Append('/').Append(encoded);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the text of a key within its keyspace: its encoded segments joined by "/".
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The key's text, such as <c>tcfg/n7</c>.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static string KeyText(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var builder = new StringBuilder();

        for (var index = 0; index < key.Segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('/');
            }

            var encoded = EncodeSegment(key.Segments[index]);
            builder.Append(encoded);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a text that <see cref="KeyText"/> wrote: the inverse of <see cref="KeyText"/>.
    /// </summary>
    /// <param name="keyspace">The keyspace the key belongs to.</param>
    /// <param name="text">The key's text.</param>
    /// <param name="depth">The key's partition depth.</param>
    /// <param name="key">The key, when the text is one that <see cref="KeyText"/> writes.</param>
    /// <returns>False when a segment does not decode or the key cannot be built; true otherwise.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public static bool TryDecodeKey(
        Keyspace keyspace,
        string? text,
        int depth,
        [NotNullWhen(true)] out CoordinationKey? key
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        key = null;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split('/');
        var segments = new List<KeySegment>(parts.Length);

        foreach (var part in parts)
        {
            if (!TryDecodeSegment(part, out var segment))
            {
                return false;
            }

            segments.Add(segment);
        }

        try
        {
            key = new CoordinationKey(keyspace, segments) { PartitionDepth = depth };
            return true;
        }
        catch (ArgumentException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>: reads each unreserved character as its own byte and each '%' and two uppercase
    /// hex digits as one byte, then decodes the bytes as strict UTF-8.
    /// </summary>
    /// <param name="escaped">The escaped text.</param>
    /// <param name="text">The text, when the escaped text is well formed.</param>
    /// <returns>False when the text is not one that <see cref="Escape"/> writes, or its bytes are not UTF-8.</returns>
    private static bool TryUnescape(string escaped, [NotNullWhen(true)] out string? text)
    {
        text = null;
        var bytes = new List<byte>(escaped.Length);

        for (var index = 0; index < escaped.Length; index++)
        {
            var character = escaped[index];

            if (IsUnreserved(character))
            {
                bytes.Add((byte)character);
            }
            else if (character == '%' && index + 2 < escaped.Length)
            {
                var high = HexValue(escaped[index + 1]);
                var low = HexValue(escaped[index + 2]);

                if (high < 0 || low < 0)
                {
                    return false;
                }

                bytes.Add((byte)((high << 4) | low));
                index += 2;
            }
            else
            {
                return false;
            }
        }

        try
        {
            text = StrictUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the value of one uppercase hex digit.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>The value of '0' to '9' and 'A' to 'F', and -1 for any other character.</returns>
    private static int HexValue(char character) =>
        character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };

    /// <summary>
    /// Escapes text so it holds none of the characters a key text uses as a separator: it keeps the ASCII letters,
    /// digits, '-', '.', '_' and '~', and writes every other UTF-8 byte as '%' and two uppercase hex digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var builder = new StringBuilder(bytes.Length);

        foreach (var value in bytes)
        {
            var character = (char)value;

            if (IsUnreserved(character))
            {
                builder.Append(character);
            }
            else
            {
                var hex = value.ToString("X2", CultureInfo.InvariantCulture);
                builder.Append('%').Append(hex);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether an escaped value keeps a character as it is.
    /// </summary>
    /// <param name="character">The character, from one UTF-8 byte.</param>
    /// <returns>True for an ASCII letter or digit, '-', '.', '_' and '~'.</returns>
    private static bool IsUnreserved(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
        || character is '-' or '.' or '_' or '~';
}
