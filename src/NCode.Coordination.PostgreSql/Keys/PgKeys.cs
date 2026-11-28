// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NCode.Coordination.PostgreSql.Keys;

/// <summary>
/// Encodes keyspaces and keys as the names and texts that the provider's tables store.
/// </summary>
internal static class PgKeys
{
    /// <summary>
    /// The most UTF-8 bytes of an encoded segment that are hashed, escaped or unescaped on the stack; a longer
    /// segment uses a rented buffer.
    /// </summary>
    private const int MaxStackBytes = 256;

    /// <summary>
    /// The character that separates the encoded segments of a key's text.
    /// </summary>
    private const char SegmentSeparator = '/';

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
        var span = text.AsSpan();
        return TryDecodeSegment(span, out segment);
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

        var span = text.AsSpan();
        var capacity = span.Count(SegmentSeparator) + 1;
        var segments = new List<KeySegment>(capacity);

        foreach (var range in span.Split(SegmentSeparator))
        {
            var part = span[range];

            if (!TryDecodeSegment(part, out var segment))
            {
                return false;
            }

            segments.Add(segment);
        }

        return TryBuildKey(keyspace, segments, depth, out key);
    }

    /// <summary>
    /// Returns the index value of a key: the SHA-256 digest of each encoded segment's UTF-8 bytes, concatenated, so
    /// that a key's index starts with the index of every prefix of its segments.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The index value, 32 bytes for each segment.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static byte[] Index(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var index = new byte[key.Segments.Length * SHA256.HashSizeInBytes];

        for (var position = 0; position < key.Segments.Length; position++)
        {
            var encoded = EncodeSegment(key.Segments[position]);
            var destination = index.AsSpan(
                position * SHA256.HashSizeInBytes,
                SHA256.HashSizeInBytes
            );
            HashSegment(encoded, destination);
        }

        return index;
    }

    /// <summary>
    /// Returns the least byte string that is greater than every byte string that starts with a prefix.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>
    /// The end of the prefix's range: the prefix without its trailing 0xFF bytes and with 1 added to the last byte
    /// left; null when no byte is left.
    /// </returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    public static byte[]? PrefixEnd(byte[] prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var length = prefix.Length;

        while (length > 0 && prefix[length - 1] == 0xFF)
        {
            length--;
        }

        if (length == 0)
        {
            return null;
        }

        var end = prefix.AsSpan(0, length).ToArray();
        end[length - 1]++;
        return end;
    }

    /// <summary>
    /// Returns the continuation token of a listed row: the Base64Url text, without padding, of one byte holding the
    /// row's depth followed by the row's index value.
    /// </summary>
    /// <param name="index">The row's index value.</param>
    /// <param name="depth">The row's depth.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentNullException">The index is null.</exception>
    public static string PageToken(byte[] index, int depth)
    {
        ArgumentNullException.ThrowIfNull(index);

        var bytes = new byte[index.Length + 1];
        bytes[0] = (byte)depth;
        index.CopyTo(bytes, 1);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Reads a token that <see cref="PageToken"/> wrote: the inverse of <see cref="PageToken"/>.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="prefix">The index value that the listed rows start with.</param>
    /// <param name="index">The row's index value, when the token is one that <see cref="PageToken"/> writes.</param>
    /// <param name="depth">The row's depth, or 0 when the token is not.</param>
    /// <returns>
    /// False unless the token decodes, holds 1 and 32 bytes for each of 1 to 16 segments, starts with a depth of at
    /// most that many segments, and holds an index value that starts with the prefix; true otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    public static bool TryReadPageToken(
        string? token,
        byte[] prefix,
        [NotNullWhen(true)] out byte[]? index,
        out int depth
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);

        index = null;
        depth = 0;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!TryDecodeTokenBytes(token, out var bytes))
        {
            return false;
        }

        var segments = (bytes.Length - 1) / SHA256.HashSizeInBytes;

        if (segments > CoordinationKey.MaxSegments || bytes[0] > segments)
        {
            return false;
        }

        var candidate = bytes.AsSpan(1).ToArray();

        if (!candidate.AsSpan().StartsWith(prefix))
        {
            return false;
        }

        index = candidate;
        depth = bytes[0];
        return true;
    }

    /// <summary>
    /// Decodes a token's Base64Url text and checks that it holds a depth and whole index digests.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="bytes">The decoded bytes, when the token has the right shape.</param>
    /// <returns>False when the token does not decode, or its length is not 1 and a multiple of 32; true
    /// otherwise.</returns>
    private static bool TryDecodeTokenBytes(string token, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        byte[] decoded;

        try
        {
            decoded = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (
            decoded.Length < 1 + SHA256.HashSizeInBytes
            || (decoded.Length - 1) % SHA256.HashSizeInBytes != 0
        )
        {
            return false;
        }

        bytes = decoded;
        return true;
    }

    /// <summary>
    /// Builds a key from decoded segments.
    /// </summary>
    /// <param name="keyspace">The keyspace the key belongs to.</param>
    /// <param name="segments">The decoded segments.</param>
    /// <param name="depth">The key's partition depth.</param>
    /// <param name="key">The key, when the segments and the depth make one.</param>
    /// <returns>False when the key cannot be built; true otherwise.</returns>
    private static bool TryBuildKey(
        Keyspace keyspace,
        List<KeySegment> segments,
        int depth,
        [NotNullWhen(true)] out CoordinationKey? key
    )
    {
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
    /// Decodes one encoded segment without allocating a string for it.
    /// </summary>
    /// <param name="text">The encoded segment.</param>
    /// <param name="segment">The segment, when the text is one that <see cref="EncodeSegment"/> writes.</param>
    /// <returns>False when the text is not one that <see cref="EncodeSegment"/> writes; true otherwise.</returns>
    private static bool TryDecodeSegment(
        ReadOnlySpan<char> text,
        [NotNullWhen(true)] out KeySegment? segment
    )
    {
        segment = null;

        if (text.IsEmpty)
        {
            return false;
        }

        var rest = text[1..];

        switch (text[0])
        {
            case 't':
                return TryDecodeTextSegment(text, rest, out segment);

            case 'n':
                return TryDecodeNumberSegment(rest, out segment);

            case 'u':
                return TryDecodeUuidSegment(rest, out segment);

            default:
                return false;
        }
    }

    /// <summary>
    /// Decodes a text segment: "t" followed by escaped text.
    /// </summary>
    /// <param name="text">The whole encoded segment, which the decoded segment must encode back to.</param>
    /// <param name="rest">The encoded segment without its first letter.</param>
    /// <param name="segment">The segment, when the text is one that <see cref="EncodeSegment"/> writes.</param>
    /// <returns>False when the text is not one that <see cref="EncodeSegment"/> writes; true otherwise.</returns>
    private static bool TryDecodeTextSegment(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> rest,
        [NotNullWhen(true)] out KeySegment? segment
    )
    {
        segment = null;

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

        if (!text.Equals(canonical, StringComparison.Ordinal))
        {
            return false;
        }

        segment = textSegment;
        return true;
    }

    /// <summary>
    /// Decodes a number segment: "n" followed by the invariant decimal.
    /// </summary>
    /// <param name="rest">The encoded segment without its first letter.</param>
    /// <param name="segment">The segment, when the text is a number that round-trips.</param>
    /// <returns>False when the text is not a number that round-trips; true otherwise.</returns>
    private static bool TryDecodeNumberSegment(
        ReadOnlySpan<char> rest,
        [NotNullWhen(true)] out KeySegment? segment
    )
    {
        segment = null;

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

        if (!rest.Equals(numberText, StringComparison.Ordinal))
        {
            return false;
        }

        segment = KeySegment.FromInt64(number);
        return true;
    }

    /// <summary>
    /// Decodes a UUID segment: "u" followed by 32 lowercase hex digits.
    /// </summary>
    /// <param name="rest">The encoded segment without its first letter.</param>
    /// <param name="segment">The segment, when the text is 32 lowercase hex digits.</param>
    /// <returns>False when the text is not 32 lowercase hex digits; true otherwise.</returns>
    private static bool TryDecodeUuidSegment(
        ReadOnlySpan<char> rest,
        [NotNullWhen(true)] out KeySegment? segment
    )
    {
        segment = null;

        if (!Guid.TryParseExact(rest, "N", out var uuid))
        {
            return false;
        }

        var uuidText = uuid.ToString("N", CultureInfo.InvariantCulture);

        if (!rest.Equals(uuidText, StringComparison.Ordinal))
        {
            return false;
        }

        segment = KeySegment.FromGuid(uuid);
        return true;
    }

    /// <summary>
    /// Writes the SHA-256 digest of an encoded segment's UTF-8 bytes, without allocating for a short segment.
    /// </summary>
    /// <param name="encoded">The encoded segment.</param>
    /// <param name="destination">Receives the digest, which is 32 bytes.</param>
    private static void HashSegment(string encoded, Span<byte> destination)
    {
        var byteCount = Encoding.UTF8.GetByteCount(encoded);

        if (byteCount <= MaxStackBytes)
        {
            Span<byte> buffer = stackalloc byte[MaxStackBytes];
            var written = Encoding.UTF8.GetBytes(encoded, buffer);
            SHA256.HashData(buffer[..written], destination);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var writtenToRented = Encoding.UTF8.GetBytes(encoded, rented);
            var hashed = rented.AsSpan(0, writtenToRented);
            SHA256.HashData(hashed, destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>: reads each unreserved character as its own byte and each '%' and two uppercase
    /// hex digits as one byte, then decodes the bytes as strict UTF-8.
    /// </summary>
    /// <param name="escaped">The escaped text.</param>
    /// <param name="text">The text, when the escaped text is well formed.</param>
    /// <returns>False when the text is not one that <see cref="Escape"/> writes, or its bytes are not UTF-8.</returns>
    private static bool TryUnescape(
        ReadOnlySpan<char> escaped,
        [NotNullWhen(true)] out string? text
    )
    {
        if (escaped.Length <= MaxStackBytes)
        {
            Span<byte> buffer = stackalloc byte[MaxStackBytes];
            return TryUnescapeInto(escaped, buffer, out text);
        }

        var rented = ArrayPool<byte>.Shared.Rent(escaped.Length);

        try
        {
            return TryUnescapeInto(escaped, rented, out text);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Reads an escaped text into a buffer that holds at least one byte for each of its characters, then decodes
    /// the bytes it wrote as strict UTF-8.
    /// </summary>
    /// <param name="escaped">The escaped text.</param>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <param name="text">The text, when the escaped text is well formed.</param>
    /// <returns>False when the text is not one that <see cref="Escape"/> writes, or its bytes are not UTF-8.</returns>
    private static bool TryUnescapeInto(
        ReadOnlySpan<char> escaped,
        Span<byte> buffer,
        [NotNullWhen(true)] out string? text
    )
    {
        text = null;
        var written = 0;

        for (var index = 0; index < escaped.Length; index++)
        {
            if (!TryReadEscapedByte(escaped, ref index, out var value))
            {
                return false;
            }

            buffer[written] = value;
            written++;
        }

        try
        {
            var decoded = buffer[..written];
            text = StrictUtf8.GetString(decoded);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads one byte of an escaped text: an unreserved character as itself, or a '%' and two uppercase hex digits.
    /// </summary>
    /// <param name="escaped">The escaped text.</param>
    /// <param name="index">The position of the byte's first character; on success, the position of its last.</param>
    /// <param name="value">The byte.</param>
    /// <returns>False when the character at the position is neither unreserved nor a well-formed escape.</returns>
    private static bool TryReadEscapedByte(
        ReadOnlySpan<char> escaped,
        ref int index,
        out byte value
    )
    {
        value = 0;
        var character = escaped[index];

        if (IsUnreserved(character))
        {
            value = (byte)character;
            return true;
        }

        if (character != '%' || index + 2 >= escaped.Length)
        {
            return false;
        }

        var high = HexValue(escaped[index + 1]);
        var low = HexValue(escaped[index + 2]);

        if (high < 0 || low < 0)
        {
            return false;
        }

        value = (byte)((high << 4) | low);
        index += 2;
        return true;
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
        var byteCount = Encoding.UTF8.GetByteCount(text);

        if (byteCount <= MaxStackBytes)
        {
            Span<byte> buffer = stackalloc byte[MaxStackBytes];
            var written = Encoding.UTF8.GetBytes(text, buffer);
            return EscapeBytes(buffer[..written]);
        }

        var rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var writtenToRented = Encoding.UTF8.GetBytes(text, rented);
            var bytes = rented.AsSpan(0, writtenToRented);
            return EscapeBytes(bytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Writes UTF-8 bytes as escaped text.
    /// </summary>
    /// <param name="bytes">The UTF-8 bytes of a text.</param>
    /// <returns>The escaped text.</returns>
    private static string EscapeBytes(ReadOnlySpan<byte> bytes)
    {
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
