// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Partitions.Tests;

public sealed class RendezvousHashTests
{
    #region Score Tests

    // The expected values come from PowerShell's [Security.Cryptography.SHA256]::HashData over the UTF-8 bytes of the
    // node id followed by the partition as a 4-byte big-endian integer, read as a big-endian UInt64 from the first 8 bytes.
    [Theory]
    [InlineData("node-a", 0, 14191816837779777936UL)]
    [InlineData("node-b", 0, 16964393671946272225UL)]
    [InlineData("node-a", 7, 7629343482365091951UL)]
    [InlineData("node-b", 7, 9363059849026109644UL)]
    [InlineData("node-c", 7, 6353529055351352552UL)]
    public void Score_WhenGivenANodeAndAPartition_ReturnsTheKnownScore(
        string nodeId,
        int partition,
        ulong expected
    )
    {
        var score = RendezvousHash.Score(nodeId, partition);

        Assert.Equal(expected, score);
    }

    [Fact]
    public void Score_WhenTheNodeIdIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RendezvousHash.Score(null!, 0));
    }

    [Fact]
    public void Score_WhenThePartitionIsNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RendezvousHash.Score("node-a", -1));
    }

    #endregion
}
