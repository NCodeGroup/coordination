// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Keys;

/// <summary>
/// Holds the index value and the depth that name a keyed row among the rows one statement read; two identities are
/// equal when their depths are equal and their index bytes are.
/// </summary>
/// <param name="Index">The row's index value.</param>
/// <param name="Depth">The row's partition depth.</param>
internal readonly record struct PgRowIdentity(byte[] Index, short Depth)
{
    /// <summary>
    /// Tells whether another identity names the same row: its depth and its index bytes are equal.
    /// </summary>
    /// <param name="other">The other identity.</param>
    /// <returns><see langword="true"/> when the identities are equal.</returns>
    public bool Equals(PgRowIdentity other) =>
        Depth == other.Depth && Index.AsSpan().SequenceEqual(other.Index);

    /// <summary>
    /// Gets a hash code of the index bytes and the depth, equal for equal identities.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Index);
        hash.Add(Depth);

        return hash.ToHashCode();
    }
}
