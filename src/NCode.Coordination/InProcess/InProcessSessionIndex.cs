// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The items bound to each session, so that a session's end finds what it holds without a scan.
/// </summary>
/// <remarks>
/// The index is not thread-safe: its owner guards it with the lock of the provider.
/// </remarks>
/// <typeparam name="TItem">The type of the items that bind to a session.</typeparam>
internal class InProcessSessionIndex<TItem>
    where TItem : notnull
{
    /// <summary>
    /// Gets the items bound to each session that has any.
    /// </summary>
    private Dictionary<SessionId, HashSet<TItem>> Items { get; } = [];

    /// <summary>
    /// Binds an item to a session. Binding an item that is already bound does nothing.
    /// </summary>
    /// <param name="session">The session the item is bound to.</param>
    /// <param name="item">The item to bind.</param>
    public void Bind(SessionId session, TItem item)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Items.TryGetValue(session, out var items))
        {
            items = [];
            Items[session] = items;
        }

        items.Add(item);
    }

    /// <summary>
    /// Removes the binding of an item to a session, and forgets the session once it holds no item.
    /// </summary>
    /// <param name="session">The session the item was bound to.</param>
    /// <param name="item">The item to unbind.</param>
    public void Unbind(SessionId session, TItem item)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Items.TryGetValue(session, out var items))
        {
            return;
        }

        items.Remove(item);
        if (items.Count == 0)
        {
            Items.Remove(session);
        }
    }

    /// <summary>
    /// Removes a session and the items bound to it.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>A copy of the items that were bound to the session, or an empty array when it had none.</returns>
    public TItem[] Take(SessionId session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Items.Remove(session, out var items))
        {
            return [];
        }

        var taken = new TItem[items.Count];
        items.CopyTo(taken);

        return taken;
    }
}
