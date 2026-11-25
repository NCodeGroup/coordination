// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination.InProcess;

/// <summary>
/// A table of entries that may end after a time to live, hiding an ended entry at once and removing it lazily.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <param name="time">The clock the entries' expiries are measured on.</param>
/// <remarks>
/// The table is not thread-safe: its callers hold their own lock around every call. An ended entry is removed when
/// it is read or removed, and every <c>Math.Max(SweepFloor, Count)</c> writes the table sweeps all ended entries, so
/// memory stays bounded.
/// </remarks>
internal class InProcessExpiringTable<TKey, TValue>(TimeProvider time)
    where TKey : notnull
{
    /// <summary>
    /// The least number of writes between two sweeps.
    /// </summary>
    internal const int SweepFloor = 1_024;

    /// <summary>
    /// Gets the clock the entries' expiries are measured on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the entries by key, each with its value and its expiry, which is null when the entry never ends.
    /// </summary>
    private Dictionary<TKey, (TValue Value, InProcessExpiry? Expiry)> Entries { get; } = [];

    /// <summary>
    /// Gets or sets the number of writes since the last sweep.
    /// </summary>
    private int WritesSinceSweep { get; set; }

    /// <summary>
    /// Gets the number of entries held, including ended entries that are not yet removed.
    /// </summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Reads the value of a live entry, removing the entry when it has ended.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value of the entry when it is live.</param>
    /// <returns>True when the entry exists and has not ended.</returns>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Entries.TryGetValue(key, out var entry))
        {
            value = default;
            return false;
        }

        if (HasEnded(entry.Expiry))
        {
            Entries.Remove(key);
            value = default;
            return false;
        }

        value = entry.Value;
        return true;
    }

    /// <summary>
    /// Sets the value and the expiry of an entry, replacing any entry of the key.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value of the entry.</param>
    /// <param name="expiry">The expiry of the entry, or null when it never ends.</param>
    public void Set(TKey key, TValue value, InProcessExpiry? expiry)
    {
        ArgumentNullException.ThrowIfNull(key);

        Entries[key] = (value, expiry);

        WritesSinceSweep++;
        if (WritesSinceSweep >= Math.Max(SweepFloor, Entries.Count))
        {
            Sweep();
            WritesSinceSweep = 0;
        }
    }

    /// <summary>
    /// Replaces the value of an entry and keeps its expiry.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The new value of the entry.</param>
    /// <exception cref="InvalidOperationException">The table holds no entry of the key.</exception>
    public void Update(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Entries.TryGetValue(key, out var entry))
        {
            throw new InvalidOperationException("The table holds no entry of the key.");
        }

        Entries[key] = (value, entry.Expiry);
    }

    /// <summary>
    /// Removes an entry.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <returns>True when the entry existed and had not ended.</returns>
    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Entries.Remove(key, out var entry))
        {
            return false;
        }

        return !HasEnded(entry.Expiry);
    }

    /// <summary>
    /// Lists the live entries after removing the ended ones.
    /// </summary>
    /// <returns>A snapshot of every live entry, in no particular order.</returns>
    public IReadOnlyList<KeyValuePair<TKey, TValue>> ListLive()
    {
        Sweep();

        var list = new List<KeyValuePair<TKey, TValue>>(Entries.Count);
        foreach (var (key, entry) in Entries)
        {
            var pair = new KeyValuePair<TKey, TValue>(key, entry.Value);
            list.Add(pair);
        }

        return list;
    }

    /// <summary>
    /// Removes every ended entry.
    /// </summary>
    public void Sweep()
    {
        List<TKey>? ended = null;
        foreach (var (key, entry) in Entries)
        {
            if (HasEnded(entry.Expiry))
            {
                (ended ??= []).Add(key);
            }
        }

        if (ended is null)
        {
            return;
        }

        foreach (var key in ended)
        {
            Entries.Remove(key);
        }
    }

    /// <summary>
    /// Checks whether an optional expiry has ended on the table's clock.
    /// </summary>
    /// <param name="expiry">The expiry, or null when the entry never ends.</param>
    /// <returns>True when the expiry exists and has ended.</returns>
    private bool HasEnded(InProcessExpiry? expiry)
    {
        return expiry is { } value && value.HasEnded(Time);
    }
}
