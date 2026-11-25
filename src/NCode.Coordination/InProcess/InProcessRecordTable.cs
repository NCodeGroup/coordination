// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The records of the in-process provider and the revision of each keyspace, shared by the record and transaction
/// services.
/// </summary>
/// <remarks>
/// The table is not thread-safe: every method, except <see cref="Time"/>, <see cref="Gate"/>, <see cref="Deadlines"/>,
/// <see cref="Enter"/> and <see cref="Dispose"/>, is called inside <see cref="Enter"/>. A keyspace's revision is 1
/// before its first write. Each keyspace's records are held in a dictionary of their own, which, like the keyspace's
/// revision, stays once written. A record's expiry is a delete at a new revision of its keyspace.
/// </remarks>
internal class InProcessRecordTable : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessRecordTable"/> class.
    /// </summary>
    /// <param name="time">The clock the records' expiries are measured on.</param>
    public InProcessRecordTable(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        Time = time;
        Deadlines = new InProcessDeadlines(time, Gate);
    }

    /// <summary>
    /// Gets the clock the records' expiries are measured on.
    /// </summary>
    public TimeProvider Time { get; }

    /// <summary>
    /// Gets the lock that every operation on the table holds for its whole duration.
    /// </summary>
    public Lock Gate { get; } = new();

    /// <summary>
    /// Gets the deadlines of the state that <see cref="Gate"/> guards.
    /// </summary>
    public InProcessDeadlines Deadlines { get; }

    /// <summary>
    /// Gets the live records of each keyspace that was written, each with the sequence of its write.
    /// </summary>
    private Dictionary<
        Keyspace,
        Dictionary<CoordinationKey, InProcessRecordEntry>
    > RecordsByKeyspace { get; } = [];

    /// <summary>
    /// Gets the latest revision of each keyspace that was written.
    /// </summary>
    private Dictionary<Keyspace, long> Revisions { get; } = [];

    /// <summary>
    /// Gets or sets the sequence of the latest write.
    /// </summary>
    private long WriteSequence { get; set; }

    /// <summary>
    /// Takes the lock and runs the deadlines that are due.
    /// </summary>
    /// <returns>The scope that releases the lock when it is disposed.</returns>
    public Lock.Scope Enter()
    {
        var scope = Gate.EnterScope();
        try
        {
            Deadlines.RunDue();
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return scope;
    }

    /// <summary>
    /// Stops the timer of the deadlines so that no record expires after it.
    /// </summary>
    public void Dispose()
    {
        Deadlines.Dispose();
    }

    /// <summary>
    /// Finds a live record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>The record, or null when the key is absent or the record has ended.</returns>
    public StoredRecord? Find(CoordinationKey key)
    {
        if (!RecordsByKeyspace.TryGetValue(key.Keyspace, out var records))
        {
            return null;
        }

        return records.TryGetValue(key, out var entry) ? entry.Record : null;
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
    /// <param name="lifetime">How long the record lives and the session it is bound to, if any.</param>
    public void Put(StoredRecord record, RecordLifetime lifetime)
    {
        var keyspace = record.Key.Keyspace;
        if (!RecordsByKeyspace.TryGetValue(keyspace, out var records))
        {
            records = [];
            RecordsByKeyspace[keyspace] = records;
        }

        if (records.TryGetValue(record.Key, out var replaced))
        {
            Deadlines.Cancel(replaced.Deadline);
        }

        var sequence = ++WriteSequence;
        var key = record.Key;

        InProcessDeadline? deadline = null;
        if (InProcessExpiry.ForLifetime(Time, lifetime) is { } expiry)
        {
            deadline = Deadlines.Add(expiry, () => Expire(key, sequence));
        }

        records[key] = new InProcessRecordEntry(record, sequence, lifetime.Session, deadline);
    }

    /// <summary>
    /// Removes a record and cancels its deadline.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>True when a live record was removed.</returns>
    public bool Remove(CoordinationKey key)
    {
        if (
            !RecordsByKeyspace.TryGetValue(key.Keyspace, out var records)
            || !records.Remove(key, out var entry)
        )
        {
            return false;
        }

        Deadlines.Cancel(entry.Deadline);
        return true;
    }

    /// <summary>
    /// Lists the live records under a prefix.
    /// </summary>
    /// <param name="prefix">The prefix of the keys to list.</param>
    /// <returns>The entries whose key the prefix is a prefix of, in the order they were written.</returns>
    public IReadOnlyList<InProcessRecordEntry> ListLive(CoordinationKey prefix)
    {
        if (!RecordsByKeyspace.TryGetValue(prefix.Keyspace, out var records))
        {
            return [];
        }

        var entries = new List<InProcessRecordEntry>(records.Count);
        foreach (var (key, entry) in records)
        {
            if (prefix.IsPrefixOf(key))
            {
                entries.Add(entry);
            }
        }

        entries.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        return entries;
    }

    /// <summary>
    /// Ends a record whose deadline came due, as a delete at a new revision of its keyspace, unless the record was
    /// replaced since the deadline was added.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="sequence">The sequence of the write whose deadline came due.</param>
    private void Expire(CoordinationKey key, long sequence)
    {
        if (
            RecordsByKeyspace.TryGetValue(key.Keyspace, out var records)
            && records.TryGetValue(key, out var entry)
            && entry.Sequence == sequence
        )
        {
            Remove(key);
            Advance(key.Keyspace);
        }
    }
}
