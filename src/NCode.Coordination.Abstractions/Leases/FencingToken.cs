// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would be 0, below every real grant.
/// <summary>
/// The number a grant of a resource carries, greater than every earlier grant's.
/// </summary>
/// <remarks>
/// A resource that takes writes from holders compares a token with the greatest it has seen and refuses the write when
/// the token is lower.
/// </remarks>
public sealed record FencingToken : IComparable<FencingToken>
{
    /// <summary>
    /// Creates a fencing token.
    /// </summary>
    /// <param name="value">The number, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public FencingToken(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>
    /// Gets the number of the grant.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Compares this token with another.
    /// </summary>
    /// <param name="other">The token to compare with, or null.</param>
    /// <returns>
    /// A negative number when this token is lower, zero when they are equal, and a positive number when this token is
    /// higher or the other is null.
    /// </returns>
    public int CompareTo(FencingToken? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <summary>
    /// Determines whether one token is lower than another, where null is lower than any token.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when the first is lower.</returns>
    public static bool operator <(FencingToken? left, FencingToken? right) =>
        Compare(left, right) < 0;

    /// <summary>
    /// Determines whether one token is higher than another, where null is lower than any token.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when the first is higher.</returns>
    public static bool operator >(FencingToken? left, FencingToken? right) =>
        Compare(left, right) > 0;

    /// <summary>
    /// Determines whether one token is lower than or equal to another, where null is lower than any token.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when the first is lower or equal.</returns>
    public static bool operator <=(FencingToken? left, FencingToken? right) =>
        Compare(left, right) <= 0;

    /// <summary>
    /// Determines whether one token is higher than or equal to another, where null is lower than any token.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when the first is higher or equal.</returns>
    public static bool operator >=(FencingToken? left, FencingToken? right) =>
        Compare(left, right) >= 0;

    /// <summary>
    /// Returns the number as text.
    /// </summary>
    /// <returns>The number in the invariant culture.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static int Compare(FencingToken? left, FencingToken? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return left.CompareTo(right);
    }
}
