// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
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
        var depth = key.PartitionDepth.ToString(CultureInfo.InvariantCulture);
        var segments = string.Join('/', key.Segments.Select(EncodeSegment));

        return tag + ":" + kind + ":" + depth + ":" + segments;
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
    /// Escapes text so it holds none of the characters a key name uses as a separator: it keeps the ASCII letters,
    /// digits, '-', '.', '_' and '~', and writes every other UTF-8 byte as '%' and two uppercase hex digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    internal static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

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
                builder.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
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
