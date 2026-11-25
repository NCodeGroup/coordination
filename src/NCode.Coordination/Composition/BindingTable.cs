// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// The bound primitives of a composition, found by primitive and keyspace.
/// </summary>
internal sealed class BindingTable
{
    /// <summary>
    /// Creates a table, returning a failure for each primitive bound twice for one scope.
    /// </summary>
    /// <param name="bound">The bound primitives, in registration order.</param>
    /// <returns>The table of the accepted entries and the failures found.</returns>
    public static Checked<BindingTable> Create(IEnumerable<BoundPrimitive> bound)
    {
        ArgumentNullException.ThrowIfNull(bound);

        var failures = new List<string>();
        var entries = new Dictionary<Primitive, List<BoundPrimitive>>();

        foreach (var entry in bound)
        {
            if (!entries.TryGetValue(entry.Primitive, out var list))
            {
                list = [];
                entries.Add(entry.Primitive, list);
            }

            var first = list.Find(x => Equals(x.Keyspace, entry.Keyspace));
            if (first is not null)
            {
                var scope = DescribeScope(entry.Keyspace);
                failures.Add(
                    $"The primitive '{entry.Primitive}' is bound twice {scope}: to '{first.Provider}' and to '{entry.Provider}'."
                );
                continue;
            }

            list.Add(entry);
        }

        var frozen = entries.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableArray()
        );
        var table = new BindingTable(frozen);
        return new Checked<BindingTable>(table, [.. failures]);
    }

    /// <summary>
    /// Describes a scope for a message.
    /// </summary>
    /// <param name="keyspace">The keyspace, or null for the default.</param>
    /// <returns>The text of the scope.</returns>
    internal static string DescribeScope(Keyspace? keyspace) =>
        keyspace is null ? "by default" : $"for the keyspace '{keyspace}'";

    /// <summary>
    /// Gets whether a binding's keyspace covers another keyspace.
    /// </summary>
    /// <param name="scope">The keyspace a binding is declared for.</param>
    /// <param name="keyspace">The keyspace of the key being resolved.</param>
    /// <returns>True when the scope is in the keyspace's cluster and its segments are a prefix of the keyspace's.</returns>
    private static bool Covers(Keyspace scope, Keyspace keyspace)
    {
        if (!string.Equals(scope.Cluster, keyspace.Cluster, StringComparison.Ordinal))
        {
            return false;
        }

        var length = scope.Segments.Length;
        if (length > keyspace.Segments.Length)
        {
            return false;
        }

        var leading = keyspace.Segments.AsSpan(0, length);
        return scope.Segments.AsSpan().SequenceEqual(leading);
    }

    /// <summary>
    /// Creates a table over the accepted entries.
    /// </summary>
    /// <param name="entries">The accepted entries, grouped by primitive.</param>
    private BindingTable(FrozenDictionary<Primitive, ImmutableArray<BoundPrimitive>> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// Gets the accepted entries of each primitive, in registration order.
    /// </summary>
    private FrozenDictionary<Primitive, ImmutableArray<BoundPrimitive>> Entries { get; }

    /// <summary>
    /// Finds the entry that serves a primitive for a keyspace.
    /// </summary>
    /// <param name="primitive">The primitive.</param>
    /// <param name="keyspace">The keyspace, or null for the default binding only.</param>
    /// <returns>The entry with the longest covering keyspace, else the default entry, else null.</returns>
    public BoundPrimitive? Find(Primitive primitive, Keyspace? keyspace)
    {
        if (!Entries.TryGetValue(primitive, out var list))
        {
            return null;
        }

        BoundPrimitive? best = null;
        BoundPrimitive? fallback = null;

        foreach (var entry in list)
        {
            if (entry.Keyspace is null)
            {
                fallback = entry;
            }
            else if (
                keyspace is not null
                && Covers(entry.Keyspace, keyspace)
                && (
                    best?.Keyspace is null
                    || entry.Keyspace.Segments.Length > best.Keyspace.Segments.Length
                )
            )
            {
                best = entry;
            }
        }

        return best ?? fallback;
    }
}
