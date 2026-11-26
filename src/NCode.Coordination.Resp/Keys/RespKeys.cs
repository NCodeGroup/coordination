// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Keys;

/// <summary>
/// Encodes keyspaces and keys as server key names that carry the keyspace as their hash tag, so every key of one
/// keyspace hashes to the same slot.
/// </summary>
internal static class RespKeys
{
    /// <summary>
    /// The largest UTF-8 buffer, in bytes, that <see cref="Escape"/> takes from the stack.
    /// </summary>
    private const int StackBufferLength = 1024;

    /// <summary>
    /// Gets the UTF-8 encoding that throws on bytes that are not well-formed UTF-8, which <see cref="Unescape"/> uses.
    /// </summary>
    private static UTF8Encoding StrictUtf8 { get; } =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Returns the hash tag of a keyspace: its cluster and each segment joined by "/", in braces.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The hash tag, such as <c>{prod/tcell-1/n42}</c>.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public static string KeyspaceTag(Keyspace keyspace)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        var builder = new StringBuilder("{").Append(keyspace.Cluster);

        foreach (var segment in keyspace.Segments)
        {
            var encoded = EncodeSegment(segment);
            builder.Append('/').Append(encoded);
        }

        return builder.Append('}').ToString();
    }

    /// <summary>
    /// Returns the server key name of one piece of state that belongs to a keyspace as a whole.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="kind">The kind of state: 1 or more of 'a' to 'z', '0' to '9' and '-'.</param>
    /// <returns>The key name: the keyspace's hash tag, a ":" and the kind.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the kind is null.</exception>
    /// <exception cref="ArgumentException">The kind is empty or has a character that is not allowed.</exception>
    public static RedisKey ForKeyspace(Keyspace keyspace, string kind)
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ValidateKind(kind);

        var tag = KeyspaceTag(keyspace);

        return tag + ":" + kind;
    }

    /// <summary>
    /// Returns the server key name of one piece of state that belongs to a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="kind">The kind of state: 1 or more of 'a' to 'z', '0' to '9' and '-'.</param>
    /// <returns>
    /// The key name: the keyspace's hash tag, a ":", the kind, a ":", the partition depth, a ":" and the encoded
    /// segments joined by "/".
    /// </returns>
    /// <exception cref="ArgumentNullException">The key or the kind is null.</exception>
    /// <exception cref="ArgumentException">The kind is empty or has a character that is not allowed.</exception>
    public static RedisKey ForKey(CoordinationKey key, string kind)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKind(kind);

        var tag = KeyspaceTag(key.Keyspace);

        return tag + ":" + kind + ":" + Name(key);
    }

    /// <summary>
    /// Returns the name of a key within its keyspace: the tail that <see cref="ForKey"/> writes.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The partition depth, a ":" and the encoded segments joined by "/".</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static string Name(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var depth = key.PartitionDepth.ToString(CultureInfo.InvariantCulture);
        var encoded = key.Segments.Select(EncodeSegment);
        var segments = string.Join('/', encoded);

        return depth + ":" + segments;
    }

    /// <summary>
    /// Decodes a name that <see cref="Name"/> wrote: the inverse of <see cref="Name"/>.
    /// </summary>
    /// <param name="keyspace">The keyspace the key belongs to.</param>
    /// <param name="name">The name.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the name is null.</exception>
    /// <exception cref="FormatException">The text is not a name that <see cref="Name"/> writes.</exception>
    public static CoordinationKey DecodeName(Keyspace keyspace, string name)
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(name);

        var colon = name.IndexOf(':', StringComparison.Ordinal);

        if (colon < 1)
        {
            throw NotAName(name);
        }

        var depthText = name[..colon];

        if (
            !int.TryParse(depthText, NumberStyles.None, CultureInfo.InvariantCulture, out var depth)
        )
        {
            throw NotAName(name);
        }

        var canonical = depth.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(canonical, depthText, StringComparison.Ordinal))
        {
            throw NotAName(name);
        }

        var parts = name[(colon + 1)..].Split('/');
        var segments = parts.Select(DecodeSegment).ToList();

        try
        {
            return new CoordinationKey(keyspace, segments) { PartitionDepth = depth };
        }
        catch (ArgumentException exception)
        {
            throw NotAName(name, exception);
        }
    }

    /// <summary>
    /// Returns the start of the members of every key under a prefix: for each segment, the digest of its encoding and
    /// a "/".
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>
    /// The unpadded Base64Url of the SHA-256 of the UTF-8 of each encoded segment, each followed by "/", without the
    /// partition depth.
    /// </returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    public static string MemberPrefix(CoordinationKey prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var builder = new StringBuilder();

        foreach (var segment in prefix.Segments)
        {
            var bytes = Encoding.UTF8.GetBytes(EncodeSegment(segment));
            var digest = Base64Url.EncodeToString(SHA256.HashData(bytes));
            builder.Append(digest).Append('/');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the member that stands for a key in a keyspace's index: at most 706 characters whatever the key's
    /// segments.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The key's <see cref="MemberPrefix"/> and its partition depth.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static string Member(CoordinationKey key)
    {
        var prefix = MemberPrefix(key);
        var depth = key.PartitionDepth.ToString(CultureInfo.InvariantCulture);

        return prefix + depth;
    }

    /// <summary>
    /// Encodes one segment as a letter for its kind and its value: "t" and the escaped text, "n" and the invariant
    /// decimal, or "u" and the 32 hex digits of the UUID.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The encoded segment.</returns>
    /// <exception cref="ArgumentNullException">The segment is null.</exception>
    /// <exception cref="ArgumentException">The segment holds no value for its kind.</exception>
    internal static string EncodeSegment(KeySegment segment)
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
    /// <param name="encoded">The encoded segment.</param>
    /// <returns>The segment.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    /// <exception cref="FormatException">The text is not a segment that EncodeSegment writes.</exception>
    internal static KeySegment DecodeSegment(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (encoded.Length == 0)
        {
            throw NotEncoded(encoded);
        }

        var rest = encoded[1..];

        switch (encoded[0])
        {
            case 't':
                var text = Unescape(rest);

                if (text.Length == 0 || text.Length > KeySegment.MaxTextLength)
                {
                    throw NotEncoded(encoded);
                }

                var segment = KeySegment.FromString(text);
                var canonical = EncodeSegment(segment);

                if (!string.Equals(canonical, encoded, StringComparison.Ordinal))
                {
                    throw NotEncoded(encoded);
                }

                return segment;

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
                    throw NotEncoded(encoded);
                }

                var numberText = number.ToString(CultureInfo.InvariantCulture);

                if (!string.Equals(numberText, rest, StringComparison.Ordinal))
                {
                    throw NotEncoded(encoded);
                }

                return KeySegment.FromInt64(number);

            case 'u':
                if (!Guid.TryParseExact(rest, "N", out var uuid))
                {
                    throw NotEncoded(encoded);
                }

                var uuidText = uuid.ToString("N", CultureInfo.InvariantCulture);

                if (!string.Equals(uuidText, rest, StringComparison.Ordinal))
                {
                    throw NotEncoded(encoded);
                }

                return KeySegment.FromGuid(uuid);

            default:
                throw NotEncoded(encoded);
        }
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>: reads each unreserved character as its own byte and each '%' and two uppercase
    /// hex digits as one byte, then decodes the bytes as strict UTF-8.
    /// </summary>
    /// <param name="escaped">The escaped text.</param>
    /// <returns>The text.</returns>
    /// <exception cref="FormatException">The text is not one that <see cref="Escape"/> writes.</exception>
    private static string Unescape(string escaped)
    {
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
                    throw NotEncoded(escaped);
                }

                bytes.Add((byte)((high << 4) | low));
                index += 2;
            }
            else
            {
                throw NotEncoded(escaped);
            }
        }

        try
        {
            var array = bytes.ToArray();
            return StrictUtf8.GetString(array);
        }
        catch (DecoderFallbackException exception)
        {
            throw NotEncoded(escaped, exception);
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
    /// Creates the exception for text that is not an encoded key name.
    /// </summary>
    /// <param name="name">The text.</param>
    /// <param name="innerException">The exception that showed the text is not one, if any.</param>
    /// <returns>The exception.</returns>
    private static FormatException NotAName(string name, Exception? innerException = null) =>
        new($"'{name}' is not an encoded key name.", innerException);

    /// <summary>
    /// Creates the exception for text that is not an encoded key segment.
    /// </summary>
    /// <param name="encoded">The text.</param>
    /// <param name="innerException">The exception that showed the text is not one, if any.</param>
    /// <returns>The exception.</returns>
    private static FormatException NotEncoded(string encoded, Exception? innerException = null) =>
        new($"'{encoded}' is not an encoded key segment.", innerException);

    /// <summary>
    /// Escapes text so it holds none of the characters a key name uses as a separator: it keeps the ASCII letters,
    /// digits, '-', '.', '_' and '~', and writes every other UTF-8 byte as '%' and two uppercase hex digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    internal static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var chars = text.AsSpan();
        var maxLength = Encoding.UTF8.GetMaxByteCount(chars.Length);
        byte[]? rented = null;
        Span<byte> buffer =
            maxLength <= StackBufferLength
                ? stackalloc byte[StackBufferLength]
                : (rented = ArrayPool<byte>.Shared.Rent(maxLength));

        try
        {
            var length = Encoding.UTF8.GetBytes(chars, buffer);
            var bytes = buffer[..length];
            var builder = new StringBuilder(length);

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
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Determines whether an escaped value keeps a character as it is.
    /// </summary>
    /// <param name="character">The character, from one UTF-8 byte.</param>
    /// <returns>True for an ASCII letter or digit, '-', '.', '_' and '~'.</returns>
    private static bool IsUnreserved(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
        || character is '-' or '.' or '_' or '~';

    /// <summary>
    /// Checks that a kind is 1 or more of 'a' to 'z', '0' to '9' and '-'.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <exception cref="ArgumentNullException">The kind is null.</exception>
    /// <exception cref="ArgumentException">The kind is empty or has a character that is not allowed.</exception>
    private static void ValidateKind(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        foreach (var character in kind)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                throw new ArgumentException(
                    $"'{kind}' is not a kind: use 1 or more of 'a' to 'z', '0' to '9' and '-'.",
                    nameof(kind)
                );
            }
        }
    }
}
