// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Schema;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Names the <c>pg_notify</c> channel of each keyspace and builds the payloads that wake its subscribers.
/// </summary>
internal static class PgChannels
{
    /// <summary>
    /// Gets the name of a keyspace's channel: <c>ncode.</c>, the schema's hash, <c>.</c> and the keyspace's id as 16
    /// lowercase hex digits, which is 31 characters.
    /// </summary>
    /// <param name="schema">The schema that holds the keyspace.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <returns>The channel's name.</returns>
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
        "c:" + revision.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the payload that announces a message was published to a topic.
    /// </summary>
    /// <param name="topicToken">The continuation-token encoding of the topic's key.</param>
    /// <param name="sequence">The message's sequence number.</param>
    /// <returns>The payload: <c>t:</c>, the token, <c>:</c> and the sequence in invariant decimal.</returns>
    public static string TopicPayload(string topicToken, long sequence)
    {
        ArgumentNullException.ThrowIfNull(topicToken);

        return "t:" + topicToken + ":" + sequence.ToString(CultureInfo.InvariantCulture);
    }
}
