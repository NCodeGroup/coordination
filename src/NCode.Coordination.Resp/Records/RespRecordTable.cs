// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records;

/// <summary>
/// The table of a keyspace: its records and its ownership state, in twelve server keys beside its revision, which every
/// keyspace script receives in this order. <c>{tag}:revision</c> holds the keyspace's revision.
/// <c>{tag}:records</c> is a hash whose fields <c>r:&lt;member&gt;</c>, <c>n:&lt;member&gt;</c> and
/// <c>v:&lt;member&gt;</c> hold a record's revision, name and value, and whose field <c>s:&lt;member&gt;</c> holds the
/// session a record is bound to. <c>{tag}:record-index</c> is a sorted set of every member scored 0, read with
/// <c>ZRANGEBYLEX</c>. <c>{tag}:record-deadlines</c> is a sorted set of the members of records with a time to live,
/// scored with their deadline in milliseconds on the server's clock. <c>{tag}:sessions</c> is a hash from each session
/// to its time to live in milliseconds. <c>{tag}:session-deadlines</c> is a sorted set of the sessions scored with
/// their deadline, where 0 marks a session that has ended but is not yet cleaned up. <c>{tag}:session-bindings</c> is a
/// sorted set, all scored 0, of what is bound to each session: a grant field, a record member or a group member.
/// <c>{tag}:grants</c> is a hash of the holders of leases, leaderships and semaphore slots. <c>{tag}:grant-deadlines</c>
/// is a sorted set of the grant fields bound to a time to live, scored with their deadline. <c>{tag}:fencing</c> is a
/// hash of the fencing counters, which never expire. <c>{tag}:members</c> is a sorted set, all scored 0, of the members
/// of groups. <c>{tag}:member-fields</c> is a hash of each member's session, address and metadata. Every keyspace script
/// runs through <see cref="EvaluateAsync" />.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <param name="profile">The vendor the provider expects, which says how the server carries channel messages.</param>
/// <exception cref="ArgumentNullException">The server or the profile is null.</exception>
internal class RespRecordTable(RespServer server, RespProfile profile)
{
    /// <summary>
    /// The hour, in milliseconds, after the latest deadline that the table's keys expire.
    /// </summary>
    // The backstop of ADR-1027 rule 6, which amends ADR-1025 rule 7.
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
    /// The kind of server key that holds the hash of the sessions and their times to live.
    /// </summary>
    private const string SessionsKind = "sessions";

    /// <summary>
    /// The kind of server key that holds the sorted set of the sessions' deadlines.
    /// </summary>
    private const string SessionDeadlinesKind = "session-deadlines";

    /// <summary>
    /// The kind of server key that holds the sorted set of what is bound to each session.
    /// </summary>
    private const string BindingsKind = "session-bindings";

    /// <summary>
    /// The kind of server key that holds the hash of the grants.
    /// </summary>
    private const string GrantsKind = "grants";

    /// <summary>
    /// The kind of server key that holds the sorted set of the grants' deadlines.
    /// </summary>
    private const string GrantDeadlinesKind = "grant-deadlines";

    /// <summary>
    /// The kind of server key that holds the hash of the fencing counters.
    /// </summary>
    private const string FencingKind = "fencing";

    /// <summary>
    /// The kind of server key that holds the sorted set of the members of groups.
    /// </summary>
    private const string MembersKind = "members";

    /// <summary>
    /// The kind of server key that holds the hash of the members' fields.
    /// </summary>
    private const string MemberFieldsKind = "member-fields";

    /// <summary>
    /// The kind of server key whose name is the keyspace's watch channel.
    /// </summary>
    private const string WatchKind = "watch";

    /// <summary>
    /// Returns the keys every keyspace script receives.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>
    /// The revision, records, record index, record deadlines, sessions, session deadlines, session bindings, grants,
    /// grant deadlines, fencing, members and member fields keys, in that order.
    /// </returns>
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
            RespKeys.ForKeyspace(keyspace, SessionsKind),
            RespKeys.ForKeyspace(keyspace, SessionDeadlinesKind),
            RespKeys.ForKeyspace(keyspace, BindingsKind),
            RespKeys.ForKeyspace(keyspace, GrantsKind),
            RespKeys.ForKeyspace(keyspace, GrantDeadlinesKind),
            RespKeys.ForKeyspace(keyspace, FencingKind),
            RespKeys.ForKeyspace(keyspace, MembersKind),
            RespKeys.ForKeyspace(keyspace, MemberFieldsKind),
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
    /// <returns>The time to live in whole milliseconds, rounded up, 0 for persistent, or -1 for session-bound.</returns>
    /// <exception cref="ArgumentNullException">The lifetime is null.</exception>
    public static long TimeToLiveOf(RecordLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        if (lifetime.Session is not null)
        {
            return -1;
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
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the vendor the provider expects.
    /// </summary>
    private RespProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>
    /// Runs a keyspace script over a keyspace's table.
    /// </summary>
    /// <param name="script">The keyspace script.</param>
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

        var keys = Keys(keyspace);
        return await Server
            .EvaluateAsync(script, keys, values, cancellationToken)
            .ConfigureAwait(false);
    }
}
