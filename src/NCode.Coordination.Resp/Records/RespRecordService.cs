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
                return new StoredRecord(
                    key,
                    RespRecordTable.ReadValue(fields[1], reply),
                    RespRecordTable.ReadRevision(fields[0], reply)
                );

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
        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordCreate,
                key.Keyspace,
                [RespKeys.Member(key), RespKeys.Name(key), value, timeToLive],
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 0 when fields.Length == 1:
                return RecordWriteResult.Written(RespRecordTable.ReadRevision(fields[0], reply));

            case 1 when fields.Length == 0:
                return RecordWriteResult.Conflict;

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

        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordRead,
                key.Keyspace,
                [RespKeys.Member(key)],
                cancellationToken
            )
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
        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordSwap,
                key.Keyspace,
                [RespKeys.Member(key), RespKeys.Name(key), expected.Value, value, timeToLive],
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 0 when fields.Length == 1:
                return RecordWriteResult.Written(RespRecordTable.ReadRevision(fields[0], reply));

            case 1 when fields.Length == 0:
                return RecordWriteResult.Conflict;

            case 2 when fields.Length == 0:
                return RecordWriteResult.NotFound;

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

        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordDelete,
                key.Keyspace,
                [RespKeys.Member(key), expected.Value],
                cancellationToken
            )
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

        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordTake,
                key.Keyspace,
                [RespKeys.Member(key)],
                cancellationToken
            )
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

        RespCall.Check(options, cancellationToken);

        var start = token is null ? "[" + member : "(" + token;
        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordList,
                prefix.Keyspace,
                [start, "(" + member + "~", page.Limit],
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (
            status != 0
            || fields.Length < 2
            || (fields.Length - 2) % 3 != 0
            || (fields.Length - 2) / 3 > page.Limit
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

        var records = new List<StoredRecord>((fields.Length - 2) / 3);
        for (var index = 2; index < fields.Length; index += 3)
        {
            var name = (string?)fields[index];
            if (name is null)
            {
                throw RespReplies.Unexpected(reply);
            }

            CoordinationKey key;
            try
            {
                key = RespKeys.DecodeName(prefix.Keyspace, name);
            }
            catch (FormatException)
            {
                throw RespReplies.Unexpected(reply);
            }

            records.Add(
                new StoredRecord(
                    key,
                    RespRecordTable.ReadValue(fields[index + 2], reply),
                    RespRecordTable.ReadRevision(fields[index + 1], reply)
                )
            );
        }

        return new RecordPage(revision, records)
        {
            ContinuationToken = next.Length == 0 ? null : next,
        };
    }
}
