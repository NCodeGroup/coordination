// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using NCode.Coordination.PostgreSql.Schema;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Names the <c>pg_notify</c> channel of each keyspace and builds the payloads that wake its subscribers.
/// </summary>
internal static partial class PgChannels
{
    /// <summary>
    /// The text that opens the payload of a change log's announcement.
    /// </summary>
    private const string ChangePrefix = "c:";

    /// <summary>
    /// The text that opens the payload of a topic's announcement.
    /// </summary>
    private const string TopicPrefix = "t:";

    /// <summary>
    /// The character that follows the topic token in the payload of a topic's announcement.
    /// </summary>
    private const char TopicSeparator = ':';

    /// <summary>
    /// Gets the name of a keyspace's channel: <c>ncode.</c>, the schema's hash, <c>.</c> and the keyspace's id as 16
    /// lowercase hex digits, which is 31 characters.
    /// </summary>
    /// <param name="schema">The schema that holds the keyspace.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <returns>The channel's name.</returns>
    /// <exception cref="ArgumentNullException">The schema is null.</exception>
    public static string ForKeyspace(PgSchemaName schema, long keyspaceId)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return $"ncode.{schema.Hash}.{unchecked((ulong)keyspaceId):x16}";
    }

    /// <summary>
    /// Gets the payload that announces a keyspace's change log has reached a revision.
    /// </summary>
    /// <param name="revision">The last revision the transaction took.</param>
    /// <returns>The payload: <c>c:</c> and the revision in invariant decimal.</returns>
    public static string ChangePayload(long revision) =>
        ChangePrefix + revision.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the payload of a topic's announcement before the sequence, which the publish statement appends.
    /// </summary>
    /// <param name="topicToken">The continuation-token encoding of the topic's key.</param>
    /// <returns>The announcement: <c>t:</c>, the token and <c>:</c>.</returns>
    /// <exception cref="ArgumentNullException">The token is null.</exception>
    public static string TopicAnnouncement(string topicToken)
    {
        ArgumentNullException.ThrowIfNull(topicToken);

        return TopicPrefix + topicToken + TopicSeparator;
    }

    /// <summary>
    /// Gets the payload that announces a message was published to a topic.
    /// </summary>
    /// <param name="topicToken">The continuation-token encoding of the topic's key.</param>
    /// <param name="sequence">The message's sequence number.</param>
    /// <returns>The payload: <c>t:</c>, the token, <c>:</c> and the sequence in invariant decimal.</returns>
    public static string TopicPayload(string topicToken, long sequence)
    {
        var announcement = TopicAnnouncement(topicToken);
        var sequenceText = sequence.ToString(CultureInfo.InvariantCulture);

        return announcement + sequenceText;
    }

    /// <summary>
    /// Reads a payload that <see cref="ChangePayload"/> or <see cref="TopicPayload"/> built.
    /// </summary>
    /// <param name="payload">The payload of a notification.</param>
    /// <param name="wake">When this method returns true, what the payload says.</param>
    /// <returns>True when the payload is well formed; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">The payload is null.</exception>
    public static bool TryParseWake(string payload, out PgWake wake)
    {
        ArgumentNullException.ThrowIfNull(payload);

        wake = default;

        if (payload.StartsWith(ChangePrefix, StringComparison.Ordinal))
        {
            var revisionText = payload.AsSpan(ChangePrefix.Length);
            if (!TryParseNumber(revisionText, out var revision))
            {
                return false;
            }

            wake = new PgWake(null, revision);
            return true;
        }

        if (payload.StartsWith(TopicPrefix, StringComparison.Ordinal))
        {
            var rest = payload.AsSpan(TopicPrefix.Length);
            var colon = rest.IndexOf(TopicSeparator);
            if (colon <= 0 || rest[(colon + 1)..].Contains(TopicSeparator))
            {
                return false;
            }

            var sequenceText = rest[(colon + 1)..];
            if (!TryParseNumber(sequenceText, out var sequence))
            {
                return false;
            }

            var topicToken = rest[..colon].ToString();
            wake = new PgWake(topicToken, sequence);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a name is a channel that <see cref="ForKeyspace"/> builds.
    /// </summary>
    /// <param name="channel">The name to check.</param>
    /// <returns>True when the name is <c>ncode.</c>, eight hex digits, <c>.</c> and sixteen hex digits; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    public static bool IsChannel(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return ChannelPattern().IsMatch(channel);
    }

    /// <summary>
    /// Reads a non-negative invariant decimal number made of digits only.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="number">When this method returns true, the number.</param>
    /// <returns>True when the text is a non-negative <see cref="long"/>; otherwise, false.</returns>
    private static bool TryParseNumber(ReadOnlySpan<char> text, out long number)
    {
        number = 0;
        return !text.IsEmpty
            && text[0] is >= '0' and <= '9'
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Gets the pattern of a keyspace's channel name.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^ncode\.[0-9a-f]{8}\.[0-9a-f]{16}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();
}
