// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A watch on one key or on the keys under one prefix, with the queue of changes that it has not yet delivered.
/// </summary>
/// <param name="target">The key to watch, or the prefix of the keys to watch.</param>
/// <param name="isPrefix">
/// True to watch every key that the target is a prefix of; false to watch only the target.
/// </param>
/// <remarks>
/// The record table posts to the queue while it holds its lock; the watch's reader takes from the queue.
/// </remarks>
internal class InProcessRecordWatcher(CoordinationKey target, bool isPrefix)
{
    /// <summary>
    /// The number of changes the queue holds before it drops them and reports a resync.
    /// </summary>
    internal const int QueueCapacity = 1_024;

    /// <summary>
    /// Gets the key to watch, or the prefix of the keys to watch.
    /// </summary>
    private CoordinationKey Target { get; } = target;

    /// <summary>
    /// Gets a value indicating whether the watch covers every key that the target is a prefix of.
    /// </summary>
    private bool IsPrefix { get; } = isPrefix;

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
