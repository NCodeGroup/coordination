// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Transactions;

/// <summary>
/// The transactions of the RESP provider: each one is one record script that checks the conditions and applies the
/// writes of the branch they choose over the keyspace's record table.
/// </summary>
/// <param name="table">The record table that runs the record scripts.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal class RespTransactionService(RespRecordTable table) : ITransactionService
{
    /// <summary>
    /// Adds a branch's writes to the arguments of the transaction script.
    /// </summary>
    /// <param name="arguments">The arguments to add to.</param>
    /// <param name="writes">The writes of the branch.</param>
    /// <exception cref="CoordinationException">A write binds its record to a session, which the provider doesn't support.</exception>
    private static void AddBranch(List<RedisValue> arguments, ImmutableArray<RecordWrite> writes)
    {
        arguments.Add(writes.Length);

        foreach (var write in writes)
        {
            var member = RespKeys.Member(write.Key);
            var name = RespKeys.Name(write.Key);

            if (write.Lifetime is { } lifetime)
            {
                var timeToLive = RespRecordTable.TimeToLiveOf(lifetime);

                arguments.Add("p");
                arguments.Add(member);
                arguments.Add(name);
                arguments.Add(write.Value);
                arguments.Add(timeToLive);
            }
            else
            {
                arguments.Add("d");
                arguments.Add(member);
                arguments.Add(name);
                arguments.Add("");
                arguments.Add("");
            }
        }
    }

    /// <summary>
    /// Gets the record table that runs the record scripts.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

    /// <inheritdoc />
    public async ValueTask<TransactionResult> ExecuteAsync(
        RecordTransaction transaction,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        RespCall.Check(options, cancellationToken);

        List<RedisValue> arguments = [transaction.Conditions.Length];

        foreach (var condition in transaction.Conditions)
        {
            var member = RespKeys.Member(condition.Key);

            switch (condition.Kind)
            {
                case RecordConditionKind.Absent:
                    arguments.Add("a");
                    arguments.Add(member);
                    arguments.Add("");
                    break;

                case RecordConditionKind.RevisionEquals when condition.Revision is { } revision:
                    arguments.Add("r");
                    arguments.Add(member);
                    arguments.Add(revision.Value);
                    break;

                case RecordConditionKind.ValueEquals:
                    arguments.Add("v");
                    arguments.Add(member);
                    arguments.Add(condition.Value);
                    break;

                default:
                    throw new ArgumentException(
                        $"The condition kind '{condition.Kind}' is not known.",
                        nameof(transaction)
                    );
            }
        }

        AddBranch(arguments, transaction.OnSuccess);
        AddBranch(arguments, transaction.OnFailure);

        var reply = await Table
            .EvaluateAsync(
                RespScript.RecordTransaction,
                transaction.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (fields.Length != 1)
        {
            throw RespReplies.Unexpected(reply);
        }

        var resultRevision = RespRecordTable.ReadRevision(fields[0], reply);

        return status switch
        {
            0 => new TransactionResult(TransactionStatus.Succeeded, resultRevision),
            1 => new TransactionResult(TransactionStatus.Failed, resultRevision),
            _ => throw RespReplies.Unexpected(reply),
        };
    }
}
