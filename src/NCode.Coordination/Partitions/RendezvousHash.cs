// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NCode.Coordination.Partitions;

/// <summary>
/// Scores a member for a partition, so the member with the highest score owns the partition.
/// </summary>
internal static class RendezvousHash
{
    /// <summary>
    /// Returns the weight of a member for a partition.
    /// </summary>
    /// <param name="nodeId">The member's node identifier.</param>
    /// <param name="partition">The partition number.</param>
    /// <returns>The first 8 bytes, big-endian, of the SHA-256 hash of the node identifier and the partition.</returns>
    /// <exception cref="ArgumentNullException">The node identifier is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The partition is negative.</exception>
    public static ulong Score(string nodeId, int partition)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(partition);

        Span<byte> buffer =
            stackalloc byte[Encoding.UTF8.GetMaxByteCount(nodeId.Length) + sizeof(int)];
        var nodeLength = Encoding.UTF8.GetBytes(nodeId, buffer);
        BinaryPrimitives.WriteInt32BigEndian(buffer[nodeLength..], partition);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(buffer[..(nodeLength + sizeof(int))], hash);

        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}
