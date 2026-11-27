// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records;

/// <summary>
/// A line of an event on a keyspace's watch channel: a kind, <c>w</c> for a record written, <c>d</c> for a record
/// deleted or <c>m</c> for a group whose members changed, and a name, the record's name or the group's member.
/// </summary>
/// <param name="Kind">The kind of the line.</param>
/// <param name="Name">The record's name or the group's member.</param>
internal readonly record struct RespWatchLine(char Kind, string Name)
{
    /// <summary>
    /// Parses the lines of an event that follow its revision.
    /// </summary>
    /// <param name="message">The event's message: the revision, then one line for each change.</param>
    /// <param name="lines">The lines after the revision, or empty when the message is malformed.</param>
    /// <returns>True when every line is well formed, otherwise false.</returns>
    public static bool TryParse(RedisValue message, out ImmutableArray<RespWatchLine> lines)
    {
        lines = [];

        var text = (string?)message;
        if (text is null)
        {
            return false;
        }

        var rest = text.AsSpan();
        var count = rest.Count('\n');
        var builder = ImmutableArray.CreateBuilder<RespWatchLine>(count);
        var feed = rest.IndexOf('\n');
        while (feed >= 0)
        {
            rest = rest[(feed + 1)..];
            feed = rest.IndexOf('\n');
            var part = feed < 0 ? rest : rest[..feed];
            if (part.Length < 2)
            {
                return false;
            }

            var name = part[1..].ToString();
            var line = new RespWatchLine(part[0], name);
            builder.Add(line);
        }

        lines = builder.MoveToImmutable();
        return true;
    }
}
