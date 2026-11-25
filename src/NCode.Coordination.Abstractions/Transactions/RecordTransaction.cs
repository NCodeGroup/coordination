// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// Conditions on several records and the writes to apply: one set when every condition holds, another when one does
/// not.
/// </summary>
/// <remarks>
/// All keys of a transaction share one keyspace. A branch writes each key at most once.
/// </remarks>
public sealed class RecordTransaction
{
    private const string OneKeyspaceMessage = "Every key of a transaction is in one keyspace.";

    /// <summary>
    /// Creates a transaction.
    /// </summary>
    /// <param name="conditions">
    /// The conditions that must all hold, at most <see cref="CoordinationLimits.MaxTransactionConditions"/>, possibly
    /// none.
    /// </param>
    /// <param name="onSuccess">
    /// The writes to apply when every condition holds, 1 to <see cref="CoordinationLimits.MaxTransactionWrites"/>, each
    /// key at most once.
    /// </param>
    /// <exception cref="ArgumentNullException">The conditions or the writes are null.</exception>
    /// <exception cref="ArgumentException">
    /// A list holds a null element, there are too many conditions, the writes are too few or too many or write a key
    /// twice, or a key is in another keyspace than the first write's.
    /// </exception>
    public RecordTransaction(
        IEnumerable<RecordCondition> conditions,
        IEnumerable<RecordWrite> onSuccess
    )
    {
        Conditions = ElementList.Copy(conditions, nameof(conditions));

        if (Conditions.Length > CoordinationLimits.MaxTransactionConditions)
        {
            throw new ArgumentException(
                $"A transaction has at most {CoordinationLimits.MaxTransactionConditions} conditions, and this one has {Conditions.Length}.",
                nameof(conditions)
            );
        }

        OnSuccess = ElementList.Copy(onSuccess, nameof(onSuccess));
        CheckBranch(OnSuccess, 1, nameof(onSuccess));
        Keyspace = OnSuccess[0].Key.Keyspace;

        foreach (var condition in Conditions)
        {
            if (!Keyspace.Equals(condition.Key.Keyspace))
            {
                throw new ArgumentException(OneKeyspaceMessage, nameof(conditions));
            }
        }

        CheckKeyspace(OnSuccess, nameof(onSuccess));
    }

    /// <summary>
    /// Gets the keyspace of every key, by which the composition routes the transaction.
    /// </summary>
    public Keyspace Keyspace { get; }

    /// <summary>
    /// Gets the conditions that must all hold, possibly none.
    /// </summary>
    public ImmutableArray<RecordCondition> Conditions { get; }

    /// <summary>
    /// Gets the writes applied when every condition holds.
    /// </summary>
    public ImmutableArray<RecordWrite> OnSuccess { get; }

    /// <summary>
    /// Gets the writes applied when a condition does not hold, none by default.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The list is a default array or holds a null element, has more than
    /// <see cref="CoordinationLimits.MaxTransactionWrites"/> writes or writes a key twice, or a key is in another
    /// keyspace than the transaction's.
    /// </exception>
    public ImmutableArray<RecordWrite> OnFailure
    {
        get;
        init
        {
            var writes = ElementList.Check(value, nameof(OnFailure));
            CheckBranch(writes, 0, nameof(OnFailure));
            CheckKeyspace(writes, nameof(OnFailure));
            field = writes;
        }
    } = [];

    private static void CheckBranch(
        ImmutableArray<RecordWrite> writes,
        int minimum,
        string paramName
    )
    {
        if (writes.Length < minimum || writes.Length > CoordinationLimits.MaxTransactionWrites)
        {
            throw new ArgumentException(
                $"A branch has {minimum} to {CoordinationLimits.MaxTransactionWrites} writes, and this one has {writes.Length}.",
                paramName
            );
        }

        var keys = new HashSet<CoordinationKey>(writes.Length);

        foreach (var write in writes)
        {
            if (!keys.Add(write.Key))
            {
                throw new ArgumentException("A branch writes each key at most once.", paramName);
            }
        }
    }

    private void CheckKeyspace(ImmutableArray<RecordWrite> writes, string paramName)
    {
        foreach (var write in writes)
        {
            if (!Keyspace.Equals(write.Key.Keyspace))
            {
                throw new ArgumentException(OneKeyspaceMessage, paramName);
            }
        }
    }
}
