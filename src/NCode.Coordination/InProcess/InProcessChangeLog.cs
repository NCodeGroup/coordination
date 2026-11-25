// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The latest record changes of one keyspace, oldest first, without values.
/// </summary>
/// <remarks>
/// The log is not thread-safe: its owner guards it with the lock that guards the keyspace's records.
/// </remarks>
internal class InProcessChangeLog
{
    /// <summary>
    /// The number of changes the log keeps before it drops the oldest.
    /// </summary>
    internal const int Capacity = 1_024;

    /// <summary>
    /// Gets the latest revision whose changes the log may be missing.
    /// </summary>
    public long Floor { get; private set; } = 1;

    /// <summary>
    /// Gets the changes the log holds, oldest first.
    /// </summary>
    private Queue<InProcessLoggedChange> Changes { get; } = new();

    /// <summary>
    /// Appends a change, dropping the oldest change when the log is full.
    /// </summary>
    /// <param name="change">The change to append, at a later revision than any change the log holds.</param>
    public void Append(InProcessLoggedChange change)
    {
        if (Changes.Count == Capacity)
        {
            var dropped = Changes.Dequeue();
            Floor = dropped.Revision.Value;
        }

        Changes.Enqueue(change);
    }

    /// <summary>
    /// Determines whether the log holds every change after a revision.
    /// </summary>
    /// <param name="after">The revision after which the changes are wanted.</param>
    /// <returns>True when no change after the revision has been dropped.</returns>
    public bool Covers(Revision after)
    {
        ArgumentNullException.ThrowIfNull(after);

        return after.Value >= Floor;
    }

    /// <summary>
    /// Gets the changes after a revision, in log order.
    /// </summary>
    /// <param name="after">The revision after which the changes are wanted.</param>
    /// <returns>The changes whose revision is later than the given one.</returns>
    public IReadOnlyList<InProcessLoggedChange> After(Revision after)
    {
        ArgumentNullException.ThrowIfNull(after);

        var result = new List<InProcessLoggedChange>();
        foreach (var change in Changes)
        {
            if (change.Revision > after)
            {
                result.Add(change);
            }
        }

        return result;
    }
}
