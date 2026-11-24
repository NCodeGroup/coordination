// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The part of the key space one application owns: a cluster name followed by zero or more typed segments.
/// </summary>
/// <remarks>
/// Two keyspaces are equal when their clusters are equal, compared ordinally, and their segments are equal in the same
/// order. Its text form is the cluster, then a "/" and each segment, such as <c>prod/cell-1/42</c>.
/// </remarks>
public sealed partial record Keyspace
{
    /// <summary>
    /// The longest cluster name, in characters.
    /// </summary>
    public const int MaxClusterLength = 63;

    /// <summary>
    /// The most segments a keyspace holds.
    /// </summary>
    public const int MaxSegments = 8;

    /// <summary>
    /// Creates a keyspace.
    /// </summary>
    /// <param name="cluster">
    /// The cluster name: 1 to <see cref="MaxClusterLength"/> lowercase letters, digits and '-', starting and ending
    /// with a letter or digit.
    /// </param>
    /// <param name="segments">The segments after the cluster, at most <see cref="MaxSegments"/> and none null.</param>
    /// <exception cref="ArgumentNullException">The cluster or the segments are null.</exception>
    /// <exception cref="ArgumentException">
    /// The cluster is not a valid name, a segment is null, or there are more than <see cref="MaxSegments"/> segments.
    /// </exception>
    public Keyspace(string cluster, params IEnumerable<KeySegment> segments)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (!ClusterPattern().IsMatch(cluster))
        {
            throw new ArgumentException(
                $"'{cluster}' is not a cluster name: use 1 to {MaxClusterLength} lowercase letters, digits and '-', starting and ending with a letter or digit.",
                nameof(cluster)
            );
        }

        var copy = ElementList.Copy(segments, nameof(segments));

        if (copy.Length > MaxSegments)
        {
            throw new ArgumentException(
                $"A keyspace has at most {MaxSegments} segments, and this one has {copy.Length}.",
                nameof(segments)
            );
        }

        Cluster = cluster;
        Segments = copy;
    }

    /// <summary>
    /// Gets the cluster name.
    /// </summary>
    public string Cluster { get; }

    /// <summary>
    /// Gets the segments after the cluster, which may be none.
    /// </summary>
    public ImmutableArray<KeySegment> Segments { get; }

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClusterPattern();

    /// <summary>
    /// Creates a key in this keyspace.
    /// </summary>
    /// <param name="segments">
    /// The segments after the keyspace: at least one, at most <see cref="CoordinationKey.MaxSegments"/>, none null.
    /// </param>
    /// <returns>The key, with a partition depth of 0.</returns>
    /// <exception cref="ArgumentNullException">The segments are null.</exception>
    /// <exception cref="ArgumentException">
    /// A segment is null, or there are no segments or more than <see cref="CoordinationKey.MaxSegments"/> of them.
    /// </exception>
    public CoordinationKey CreateKey(params IEnumerable<KeySegment> segments) =>
        new(this, segments);

    /// <summary>
    /// Determines whether another keyspace has the same cluster and the same segments in the same order.
    /// </summary>
    /// <param name="other">The keyspace to compare with.</param>
    /// <returns>True when the keyspaces are equal.</returns>
    public bool Equals(Keyspace? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        var segments = Segments.AsSpan();
        var otherSegments = other.Segments.AsSpan();

        return string.Equals(Cluster, other.Cluster, StringComparison.Ordinal)
            && segments.SequenceEqual(otherSegments);
    }

    /// <summary>
    /// Returns a hash code over the cluster and the segments.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Cluster, StringComparer.Ordinal);

        foreach (var segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the cluster, then a "/" and each segment's text, such as <c>prod/cell-1/42</c>.
    /// </summary>
    /// <returns>The keyspace as text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder(Cluster);

        foreach (var segment in Segments)
        {
            var text = segment.ToString();
            builder.Append('/').Append(text);
        }

        return builder.ToString();
    }
}
