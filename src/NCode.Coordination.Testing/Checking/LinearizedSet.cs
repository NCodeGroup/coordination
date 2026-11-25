// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// An immutable set of call indexes that a search has linearized, held as bits so that equal sets compare by value.
/// </summary>
internal sealed class LinearizedSet : IEquatable<LinearizedSet>
{
    /// <summary>
    /// The number of indexes that one word of the set holds.
    /// </summary>
    private const int WordBits = 64;

    /// <summary>
    /// Creates a set over bits that the caller leaves unchanged.
    /// </summary>
    /// <param name="words">The bits of the set, which the set takes without copying.</param>
    private LinearizedSet(ulong[] words)
    {
        Words = words;
    }

    /// <summary>
    /// Gets the bits of the set, which are never mutated after construction.
    /// </summary>
    private ulong[] Words { get; }

    /// <summary>
    /// Creates a set that holds no index, sized for a number of calls.
    /// </summary>
    /// <param name="count">The number of calls whose indexes the set may hold.</param>
    /// <returns>An empty set.</returns>
    public static LinearizedSet Empty(int count) =>
        new(new ulong[(count + WordBits - 1) / WordBits]);

    /// <summary>
    /// Determines whether the set holds an index.
    /// </summary>
    /// <param name="index">The index of a call.</param>
    /// <returns>True when the set holds the index.</returns>
    public bool Contains(int index) => (Words[index / WordBits] & (1UL << (index % WordBits))) != 0;

    /// <summary>
    /// Creates a set that holds this set's indexes and one more, leaving this set as it was.
    /// </summary>
    /// <param name="index">The index of the call to add.</param>
    /// <returns>A new set.</returns>
    public LinearizedSet Add(int index)
    {
        ulong[] words = [.. Words];
        words[index / WordBits] |= 1UL << (index % WordBits);

        return new LinearizedSet(words);
    }

    /// <summary>
    /// Determines whether another set holds the same indexes.
    /// </summary>
    /// <param name="other">The set to compare with.</param>
    /// <returns>True when the sets hold the same indexes.</returns>
    public bool Equals(LinearizedSet? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Words.AsSpan().SequenceEqual(other.Words);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as LinearizedSet);

    /// <summary>
    /// Returns a hash code over the words of the set.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var word in Words)
        {
            hash.Add(word);
        }

        return hash.ToHashCode();
    }
}
