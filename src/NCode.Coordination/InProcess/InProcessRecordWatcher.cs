// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A watch on one key or on the keys under one prefix, with the queue of changes that it has not yet delivered.
/// </summary>
/// <remarks>
/// The record table posts to the queue while it holds its lock; the watch's reader takes from the queue.
/// </remarks>
internal class InProcessRecordWatcher
{
    /// <summary>
    /// The number of changes the queue holds before it drops them and reports a resync.
    /// </summary>
    internal const int QueueCapacity = 1_024;

    /// <summary>
    /// Creates a watch on one key.
    /// </summary>
    /// <param name="key">The key to watch.</param>
    /// <returns>A watch that covers only the key.</returns>
    public static InProcessRecordWatcher ForKey(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new InProcessRecordWatcher(key, isPrefix: false);
    }

    /// <summary>
    /// Creates a watch on the keys under one prefix.
    /// </summary>
    /// <param name="prefix">The prefix of the keys to watch.</param>
    /// <returns>A watch that covers every key that the prefix is a prefix of.</returns>
    public static InProcessRecordWatcher ForPrefix(CoordinationKey prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return new InProcessRecordWatcher(prefix, isPrefix: true);
    }

    /// <summary>
    /// Creates a watch on one key or on the keys under one prefix.
    /// </summary>
    /// <param name="target">The key to watch, or the prefix of the keys to watch.</param>
    /// <param name="isPrefix">
    /// True to watch every key that the target is a prefix of; false to watch only the target.
    /// </param>
    private InProcessRecordWatcher(CoordinationKey target, bool isPrefix)
    {
        Target = target;
        IsPrefix = isPrefix;
    }

    /// <summary>
    /// Gets the key to watch, or the prefix of the keys to watch.
    /// </summary>
    private CoordinationKey Target { get; }

    /// <summary>
    /// Gets a value indicating whether the watch covers every key that the target is a prefix of.
    /// </summary>
    private bool IsPrefix { get; }

    /// <summary>
    /// Gets the keyspace that the watch covers.
    /// </summary>
    public Keyspace Keyspace => Target.Keyspace;

    /// <summary>
    /// Gets the queue of the changes that the watch has not yet delivered.
    /// </summary>
    public InProcessEventQueue<RecordChange> Queue { get; } = new(QueueCapacity);

    /// <summary>
    /// Determines whether the watch covers a key.
    /// </summary>
    /// <param name="key">The key of a change.</param>
    /// <returns>True when the key is the target, or, for a prefix watch, is under the target.</returns>
    public bool Matches(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return IsPrefix ? Target.IsPrefixOf(key) : Target.Equals(key);
    }
}
