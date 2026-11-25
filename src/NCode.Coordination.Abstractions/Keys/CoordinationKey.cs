// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The address of one piece of coordination state: a keyspace followed by one or more typed segments.
/// </summary>
/// <remarks>
/// Two keys are equal when their keyspaces, their segments and their partition depths are equal, because a provider
/// stores the same segments under different partitions differently. Its text form is the keyspace, a ":" and the
/// segments joined by "/", such as <c>prod/cell-1:refresh/42</c>.
/// </remarks>
public sealed record CoordinationKey
{
    /// <summary>
    /// The most segments a key holds.
    /// </summary>
    public const int MaxSegments = 16;

    /// <summary>
    /// Creates a key.
    /// </summary>
    /// <param name="keyspace">The keyspace the key belongs to.</param>
    /// <param name="segments">
    /// The segments after the keyspace: at least one, at most <see cref="MaxSegments"/>, none null.
    /// </param>
    /// <exception cref="ArgumentNullException">The keyspace or the segments are null.</exception>
    /// <exception cref="ArgumentException">
    /// A segment is null, or there are no segments or more than <see cref="MaxSegments"/> of them.
    /// </exception>
    public CoordinationKey(Keyspace keyspace, params IEnumerable<KeySegment> segments)
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        var copy = ElementList.Copy(segments, nameof(segments));

        if (copy.Length is 0 or > MaxSegments)
        {
            throw new ArgumentException(
                $"A key has 1 to {MaxSegments} segments, and this one has {copy.Length}.",
                nameof(segments)
            );
        }

        Keyspace = keyspace;
        Segments = copy;
    }

    /// <summary>
    /// Gets the keyspace the key belongs to.
    /// </summary>
    public Keyspace Keyspace { get; }

    /// <summary>
    /// Gets the segments after the keyspace.
    /// </summary>
    public ImmutableArray<KeySegment> Segments { get; }

    /// <summary>
    /// Gets the number of leading segments that keys of one partition share, where 0, the default, means the key is its
    /// own partition.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is negative or greater than the number of segments.
    /// </exception>
    public int PartitionDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(PartitionDepth));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                value,
                Segments.Length,
                nameof(PartitionDepth)
            );
            field = value;
        }
    }

    /// <summary>
    /// Determines whether this key's keyspace and segments are the leading part of another key's, segment by segment.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns>
    /// True when the keyspaces are equal, the other key has at least as many segments, and its leading segments equal
    /// this key's. The partition depths do not matter.
    /// </returns>
    /// <exception cref="ArgumentNullException">The other key is null.</exception>
    public bool IsPrefixOf(CoordinationKey other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!Keyspace.Equals(other.Keyspace) || other.Segments.Length < Segments.Length)
        {
            return false;
        }

        var own = Segments.AsSpan();
        var leading = other.Segments.AsSpan(0, Segments.Length);

        return own.SequenceEqual(leading);
    }

    /// <summary>
    /// Determines whether another key has the same keyspace, segments and partition depth.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns>True when the keys are equal.</returns>
    public bool Equals(CoordinationKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!Keyspace.Equals(other.Keyspace) || PartitionDepth != other.PartitionDepth)
        {
            return false;
        }

        var own = Segments.AsSpan();
        var otherSegments = other.Segments.AsSpan();

        return own.SequenceEqual(otherSegments);
    }

    /// <summary>
    /// Returns a hash code over the keyspace, the segments and the partition depth.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Keyspace);
        hash.Add(PartitionDepth);

        foreach (var segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the keyspace, a ":" and the segments joined by "/", such as <c>prod/cell-1:refresh/42</c>.
    /// </summary>
    /// <returns>The key as text.</returns>
    public override string ToString()
    {
        var keyspace = Keyspace.ToString();
        var builder = new StringBuilder(keyspace).Append(':');

        for (var i = 0; i < Segments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('/');
            }

            var text = Segments[i].ToString();
            builder.Append(text);
        }

        return builder.ToString();
    }
}
