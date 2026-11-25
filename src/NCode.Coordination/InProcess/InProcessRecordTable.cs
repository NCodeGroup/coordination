// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The records of the in-process provider and the revision of each keyspace, shared by the record and transaction
/// services.
/// </summary>
/// <param name="time">The clock the records' expiries are measured on.</param>
/// <remarks>
/// The table is not thread-safe: every method, except <see cref="Time"/> and <see cref="Gate"/>, is called while
/// holding <see cref="Gate"/>. A keyspace's revision is 1 before its first write.
/// </remarks>
internal class InProcessRecordTable(TimeProvider time)
{
    /// <summary>
    /// Gets the clock the records' expiries are measured on.
    /// </summary>
    public TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the lock that every operation on the table holds for its whole duration.
    /// </summary>
    public Lock Gate { get; } = new();

    /// <summary>
    /// Gets the live records by key, each with the sequence of its write.
    /// </summary>
    private InProcessExpiringTable<CoordinationKey, InProcessRecordEntry> Records { get; } =
        new(time);

    /// <summary>
    /// Gets the latest revision of each keyspace that was written.
    /// </summary>
    private Dictionary<Keyspace, long> Revisions { get; } = [];

    /// <summary>
    /// Gets or sets the sequence of the latest write.
    /// </summary>
    private long WriteSequence { get; set; }

    /// <summary>
    /// Finds a live record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>The record, or null when the key is absent or the record has ended.</returns>
    public StoredRecord? Find(CoordinationKey key)
    {
        return Records.TryGet(key, out var entry) ? entry.Record : null;
    }

    /// <summary>
    /// Gets the current revision of a keyspace.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The revision of the keyspace's latest write, or 1 before its first write.</returns>
    public Revision Current(Keyspace keyspace)
    {
        var current = Revisions.GetValueOrDefault(keyspace, 1);
        return new Revision(current);
    }

    /// <summary>
    /// Advances the revision of a keyspace by one.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The keyspace's new revision.</returns>
    public Revision Advance(Keyspace keyspace)
    {
        var next = Revisions.GetValueOrDefault(keyspace, 1) + 1;
        Revisions[keyspace] = next;
        return new Revision(next);
    }

    /// <summary>
    /// Stores a record, replacing any record of its key.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <param name="expiry">The expiry of the record, or null when it never ends.</param>
    public void Put(StoredRecord record, InProcessExpiry? expiry)
    {
        var entry = new InProcessRecordEntry(record, ++WriteSequence);
        Records.Set(record.Key, entry, expiry);
    }

    /// <summary>
    /// Removes a record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>True when a live record was removed.</returns>
    public bool Remove(CoordinationKey key)
    {
        return Records.Remove(key);
    }

    /// <summary>
    /// Lists the live records under a prefix.
    /// </summary>
    /// <param name="prefix">The prefix of the keys to list.</param>
    /// <returns>The entries whose key the prefix is a prefix of, in the order they were written.</returns>
    public IReadOnlyList<InProcessRecordEntry> ListLive(CoordinationKey prefix)
    {
        var entries = new List<InProcessRecordEntry>();
        foreach (var (key, entry) in Records.ListLive())
        {
            if (prefix.IsPrefixOf(key))
            {
                entries.Add(entry);
            }
        }

        entries.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        return entries;
    }
}
