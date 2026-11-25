// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The live records of one keyspace in the order of their write sequences, so a listing seeks to where its page starts.
/// </summary>
/// <remarks>
/// The order is not thread-safe: its owner guards it with the lock of the provider.
/// </remarks>
internal class InProcessRecordOrder
{
    /// <summary>
    /// Gets the write sequences of the live records, in ascending order.
    /// </summary>
    private SortedSet<long> Sequences { get; } = [];

    /// <summary>
    /// Gets the key of the live record written at each sequence.
    /// </summary>
    private Dictionary<long, CoordinationKey> Keys { get; } = [];

    /// <summary>
    /// Adds a live record.
    /// </summary>
    /// <param name="sequence">The sequence of the record's write.</param>
    /// <param name="key">The key of the record.</param>
    public void Add(long sequence, CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Sequences.Add(sequence);
        Keys[sequence] = key;
    }

    /// <summary>
    /// Removes a record. Removing a sequence that was not added does nothing.
    /// </summary>
    /// <param name="sequence">The sequence of the record's write.</param>
    public void Remove(long sequence)
    {
        Sequences.Remove(sequence);
        Keys.Remove(sequence);
    }

    /// <summary>
    /// Lists the records written after a sequence.
    /// </summary>
    /// <param name="sequence">The sequence after which the listing starts.</param>
    /// <returns>The keys of the records whose sequence is greater than <paramref name="sequence"/>, in sequence order.</returns>
    public IEnumerable<CoordinationKey> After(long sequence)
    {
        if (sequence == long.MaxValue)
        {
            yield break;
        }

        foreach (var next in Sequences.GetViewBetween(sequence + 1, long.MaxValue))
        {
            yield return Keys[next];
        }
    }
}
