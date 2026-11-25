// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// The position of a write among the writes of a keyspace.
/// </summary>
/// <remarks>
/// A revision is greater for every later write in the same keyspace, and not necessarily consecutive. It is a positive
/// number.
/// </remarks>
public sealed record Revision : IComparable<Revision>
{
    /// <summary>
    /// Creates a revision.
    /// </summary>
    /// <param name="value">The position, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public Revision(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>
    /// Gets the position of the write.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Compares this revision with another.
    /// </summary>
    /// <param name="other">The revision to compare with, or null.</param>
    /// <returns>
    /// A negative number when this revision is earlier, zero when they are equal, and a positive number when this
    /// revision is later or the other is null.
    /// </returns>
    public int CompareTo(Revision? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <summary>
    /// Determines whether one revision is earlier than another, where null is earlier than any revision.
    /// </summary>
    /// <param name="left">The first revision.</param>
    /// <param name="right">The second revision.</param>
    /// <returns>True when the first is earlier.</returns>
    public static bool operator <(Revision? left, Revision? right) => Compare(left, right) < 0;

    /// <summary>
    /// Determines whether one revision is later than another, where null is earlier than any revision.
    /// </summary>
    /// <param name="left">The first revision.</param>
    /// <param name="right">The second revision.</param>
    /// <returns>True when the first is later.</returns>
    public static bool operator >(Revision? left, Revision? right) => Compare(left, right) > 0;

    /// <summary>
    /// Determines whether one revision is earlier than or equal to another, where null is earlier than any revision.
    /// </summary>
    /// <param name="left">The first revision.</param>
    /// <param name="right">The second revision.</param>
    /// <returns>True when the first is earlier or equal.</returns>
    public static bool operator <=(Revision? left, Revision? right) => Compare(left, right) <= 0;

    /// <summary>
    /// Determines whether one revision is later than or equal to another, where null is earlier than any revision.
    /// </summary>
    /// <param name="left">The first revision.</param>
    /// <param name="right">The second revision.</param>
    /// <returns>True when the first is later or equal.</returns>
    public static bool operator >=(Revision? left, Revision? right) => Compare(left, right) >= 0;

    /// <summary>
    /// Returns the position as a number.
    /// </summary>
    /// <returns>The position in the invariant culture.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static int Compare(Revision? left, Revision? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return left.CompareTo(right);
    }
}
