// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records;

/// <summary>
/// The records of the RESP provider: each operation is one record script that runs over the keyspace's record table.
/// </summary>
/// <param name="table">The record table that runs the record scripts.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal class RespRecordService(RespRecordTable table) : IRecordService
{
    /// <summary>
    /// The fields before a list page's records: the keyspace's revision and the continuation token.
    /// </summary>
    private const int PageHeaderLength = 2;

    /// <summary>
    /// The fields of each record on a list page: its name, its revision and its value.
    /// </summary>
    private const int ListedRecordLength = 3;

    /// <summary>
    /// Checks that a continuation token was issued for a prefix's member.
    /// </summary>
    /// <param name="member">The prefix's member.</param>
    /// <param name="page">The page request that holds the token.</param>
    /// <exception cref="ArgumentException">The token was not issued by the RESP provider for this prefix.</exception>
    private static void CheckToken(string member, RecordPageRequest page)
    {
        var token = page.ContinuationToken;
        if (
            token is not null
            && (
                token.Length <= member.Length || !token.StartsWith(member, StringComparison.Ordinal)
            )
        )
        {
            throw new ArgumentException(
                "The continuation token was not issued by the RESP provider for this prefix.",
                nameof(page)
            );
        }
    }

    /// <summary>
    /// Reads the name field of a listed record and decodes it to a key.
    /// </summary>
    /// <param name="keyspace">The keyspace of the record.</param>
    /// <param name="field">The name field.</param>
    /// <param name="reply">The script's reply.</param>
    /// <returns>The key of the record.</returns>
    /// <exception cref="CoordinationException">The name is missing or is not one the provider wrote.</exception>
    private static CoordinationKey ReadName(Keyspace keyspace, RedisResult field, RedisResult reply)
    {
        var name = (string?)field;
        if (name is null)
        {
            throw RespReplies.Unexpected(reply);
        }

        try
        {
            return RespKeys.DecodeName(keyspace, name);
        }
        catch (FormatException)
        {
            throw RespReplies.Unexpected(reply);
        }
    }

    /// <summary>
    /// Maps the reply of the list script to a page.
    /// </summary>
    /// <param name="prefix">The prefix that was listed.</param>
    /// <param name="limit">The most records the page may hold.</param>
    /// <param name="reply">The script's reply.</param>
    /// <returns>The page.</returns>
    /// <exception cref="CoordinationException">The reply is not one the list script returns.</exception>
    private static RecordPage ReadPage(CoordinationKey prefix, int limit, RedisResult reply)
    {
        var (status, fields) = RespReplies.Script(reply);
        var recordFields = fields.Length - PageHeaderLength;
        if (
            status != 0
            || recordFields < 0
            || recordFields % ListedRecordLength != 0
            || recordFields / ListedRecordLength > limit
        )
        {
            throw RespReplies.Unexpected(reply);
        }

        var revision = RespRecordTable.ReadRevision(fields[0], reply);
        var next = (string?)fields[1];
        if (next is null)
        {
            throw RespReplies.Unexpected(reply);
        }

        var records = new List<StoredRecord>(recordFields / ListedRecordLength);
        for (var index = PageHeaderLength; index < fields.Length; index += ListedRecordLength)
        {
            var key = ReadName(prefix.Keyspace, fields[index], reply);
            var recordRevision = RespRecordTable.ReadRevision(fields[index + 1], reply);
            var recordValue = RespRecordTable.ReadValue(fields[index + 2], reply);
            var record = new StoredRecord(key, recordValue, recordRevision);
            records.Add(record);
        }

        return new RecordPage(revision, records)
        {
            ContinuationToken = next.Length == 0 ? null : next,
        };
    }

    /// <summary>
    /// Checks that a value is no longer than the largest value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="paramName">The name of the parameter that holds the value.</param>
    /// <exception cref="ArgumentException">The value is longer than <see cref="CoordinationLimits.MaxValueLength"/> bytes.</exception>
    private static void CheckValue(ReadOnlyMemory<byte> value, string paramName)
    {
        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                paramName
            );
        }
    }

    /// <summary>
    /// Maps the reply of the read and take scripts to a record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="reply">The script's reply.</param>
    /// <returns>The record, or null when the script found none.</returns>
    /// <exception cref="CoordinationException">The reply is not one the scripts return.</exception>
    private static StoredRecord? ReadRecord(CoordinationKey key, RedisResult reply)
    {
        var (status, fields) = RespReplies.Script(reply);

        switch (status)
        {
            case 0 when fields.Length == 2:
                var revision = RespRecordTable.ReadRevision(fields[0], reply);
                var value = RespRecordTable.ReadValue(fields[1], reply);
                return new StoredRecord(key, value, revision);

            case 1 when fields.Length == 0:
                return null;

            default:
                throw RespReplies.Unexpected(reply);
        }
    }

    /// <summary>
    /// Gets the record table that runs the record scripts.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

    /// <inheritdoc />
    public async ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);
        CheckValue(value, nameof(value));
        RespCall.Check(options, cancellationToken);

        var timeToLive = RespRecordTable.TimeToLiveOf(lifetime);
        var member = RespKeys.Member(key);
        var name = RespKeys.Name(key);

        var session = RespRecordTable.SessionOf(lifetime);

        RedisValue[] arguments = [member, name, value, timeToLive, session];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordCreate, key.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 0 when fields.Length == 1:
                var revision = RespRecordTable.ReadRevision(fields[0], reply);
                return RecordWriteResult.Written(revision);

            case 1 when fields.Length == 0:
                return RecordWriteResult.Conflict;

            case 2 when fields.Length == 0:
                return RecordWriteResult.SessionEnded;

            default:
                throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var member = RespKeys.Member(key);
        RedisValue[] arguments = [member];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordRead, key.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        return ReadRecord(key, reply);
    }

    /// <inheritdoc />
    public async ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(lifetime);
        CheckValue(value, nameof(value));
        RespCall.Check(options, cancellationToken);

        var timeToLive = RespRecordTable.TimeToLiveOf(lifetime);
        var member = RespKeys.Member(key);
        var name = RespKeys.Name(key);

        var session = RespRecordTable.SessionOf(lifetime);

        RedisValue[] arguments = [member, name, expected.Value, value, timeToLive, session];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordSwap, key.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 0 when fields.Length == 1:
                var revision = RespRecordTable.ReadRevision(fields[0], reply);
                return RecordWriteResult.Written(revision);

            case 1 when fields.Length == 0:
                return RecordWriteResult.Conflict;

            case 2 when fields.Length == 0:
                return RecordWriteResult.NotFound;

            case 3 when fields.Length == 0:
                return RecordWriteResult.SessionEnded;

            default:
                throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public async ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        RespCall.Check(options, cancellationToken);

        var member = RespKeys.Member(key);
        RedisValue[] arguments = [member, expected.Value];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordDelete, key.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (fields.Length != 0)
        {
            throw RespReplies.Unexpected(reply);
        }

        return status switch
        {
            0 => RecordDeleteStatus.Deleted,
            1 => RecordDeleteStatus.Conflict,
            2 => RecordDeleteStatus.NotFound,
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var member = RespKeys.Member(key);
        RedisValue[] arguments = [member];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordTake, key.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        return ReadRecord(key, reply);
    }

    /// <inheritdoc />
    public async ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(page);

        var member = RespKeys.MemberPrefix(prefix);
        CheckToken(member, page);
        RespCall.Check(options, cancellationToken);

        var token = page.ContinuationToken;
        var start = token is null ? "[" + member : "(" + token;
        var end = "(" + member + "~";
        RedisValue[] arguments = [start, end, page.Limit];
        var reply = await Table
            .EvaluateAsync(RespScript.RecordList, prefix.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        return ReadPage(prefix, page.Limit, reply);
    }
}
