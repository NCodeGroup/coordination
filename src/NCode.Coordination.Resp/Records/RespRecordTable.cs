// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records;

/// <summary>
/// The record table of a keyspace: three server keys beside the keyspace's revision. <c>{tag}:records</c> is a hash
/// whose fields <c>r:&lt;member&gt;</c>, <c>n:&lt;member&gt;</c> and <c>v:&lt;member&gt;</c> hold a record's revision,
/// name and value; <c>{tag}:record-index</c> is a sorted set of every member scored 0, read with
/// <c>ZRANGEBYLEX</c>; and <c>{tag}:record-deadlines</c> is a sorted set of the members of records with a time to live,
/// scored with their deadline in milliseconds on the server's clock. Every record script receives exactly these four
/// keys, in this order, and runs through <see cref="EvaluateAsync" />.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <param name="profile">The vendor the provider expects, which says how the server carries channel messages.</param>
/// <exception cref="ArgumentNullException">The server or the profile is null.</exception>
internal class RespRecordTable(RespServer server, RespProfile profile)
{
    /// <summary>
    /// The hour, in milliseconds, after the latest deadline that the table's keys expire (ADR-1025 rule 7).
    /// </summary>
    internal const long BackstopMilliseconds = 3_600_000;

    /// <summary>
    /// The kind of server key that holds the keyspace's revision.
    /// </summary>
    private const string RevisionKind = "revision";

    /// <summary>
    /// The kind of server key that holds the records' fields.
    /// </summary>
    private const string RecordsKind = "records";

    /// <summary>
    /// The kind of server key that holds the sorted set of every member.
    /// </summary>
    private const string IndexKind = "record-index";

    /// <summary>
    /// The kind of server key that holds the sorted set of the deadlines.
    /// </summary>
    private const string DeadlinesKind = "record-deadlines";

    /// <summary>
    /// The kind of server key whose name is the keyspace's watch channel.
    /// </summary>
    private const string WatchKind = "watch";

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the vendor the provider expects.
    /// </summary>
    private RespProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>
    /// Returns the keys every record script receives.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The revision, records, index and deadlines keys, in that order.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public static RedisKey[] Keys(Keyspace keyspace)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        return
        [
            RespKeys.ForKeyspace(keyspace, RevisionKind),
            RespKeys.ForKeyspace(keyspace, RecordsKind),
            RespKeys.ForKeyspace(keyspace, IndexKind),
            RespKeys.ForKeyspace(keyspace, DeadlinesKind),
        ];
    }

    /// <summary>
    /// Returns the name of the keyspace's watch channel.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The text of the keyspace's watch key.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    public static string WatchChannel(Keyspace keyspace)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        return RespKeys.ForKeyspace(keyspace, WatchKind).ToString();
    }

    /// <summary>
    /// Converts a record's lifetime to the time to live a record script takes.
    /// </summary>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The time to live in whole milliseconds, rounded up, or 0 for a persistent record.</returns>
    /// <exception cref="ArgumentNullException">The lifetime is null.</exception>
    /// <exception cref="CoordinationException">The lifetime is bound to a session, which the provider doesn't support.</exception>
    public static long TimeToLiveOf(RecordLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        if (lifetime.Session is not null)
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                outcomeUnknown: false,
                "The RESP provider binds no record to a session."
            );
        }

        return lifetime.TimeToLive is { } timeToLive ? RespCall.ToMilliseconds(timeToLive) : 0;
    }

    /// <summary>
    /// Reads a record's or a page's revision from a script's reply.
    /// </summary>
    /// <param name="field">The field that holds the revision.</param>
    /// <param name="reply">The whole reply, for the exception.</param>
    /// <returns>The revision.</returns>
    /// <exception cref="CoordinationException">The field is not an integer of 1 or more.</exception>
    public static Revision ReadRevision(RedisResult field, RedisResult reply)
    {
        var value = RespReplies.Integer(field);
        if (value < 1)
        {
            throw RespReplies.Unexpected(reply);
        }

        return new Revision(value);
    }

    /// <summary>
    /// Reads a record's value from a script's reply.
    /// </summary>
    /// <param name="field">The field that holds the value.</param>
    /// <param name="reply">The whole reply, for the exception.</param>
    /// <returns>The value's bytes.</returns>
    /// <exception cref="CoordinationException">The field is null or longer than the largest value.</exception>
    public static byte[] ReadValue(RedisResult field, RedisResult reply)
    {
        var value = field.IsNull ? null : (byte[]?)field;
        if (value is null || value.Length > CoordinationLimits.MaxValueLength)
        {
            throw RespReplies.Unexpected(reply);
        }

        return value;
    }

    /// <summary>
    /// Runs a record script over a keyspace's table.
    /// </summary>
    /// <param name="script">The record script.</param>
    /// <param name="keyspace">The keyspace whose table the script works on.</param>
    /// <param name="arguments">The operation's own arguments, which follow the three common ones.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns>The result of the script.</returns>
    /// <exception cref="ArgumentNullException">The script, the keyspace or the arguments are null.</exception>
    /// <exception cref="CoordinationException">The call failed; the exception says whether it may have taken effect.</exception>
    public async ValueTask<RedisResult> EvaluateAsync(
        RespScript script,
        Keyspace keyspace,
        IReadOnlyList<RedisValue> arguments,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(arguments);

        var values = new RedisValue[arguments.Count + 3];
        values[0] = WatchChannel(keyspace);
        values[1] = Profile.ShardedChannels ? "1" : "0";
        values[2] = BackstopMilliseconds;
        for (var index = 0; index < arguments.Count; index++)
        {
            values[index + 3] = arguments[index];
        }

        return await Server
            .EvaluateAsync(script, Keys(keyspace), values, cancellationToken)
            .ConfigureAwait(false);
    }
}
