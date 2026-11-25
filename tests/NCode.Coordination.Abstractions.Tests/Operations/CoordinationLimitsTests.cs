// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CoordinationLimitsTests
{
    #region Constants Tests

    [Theory]
    [InlineData("MaxTokenLength", 128)]
    [InlineData("MaxContinuationTokenLength", 1_024)]
    [InlineData("MaxValueLength", 65_536)]
    [InlineData("MaxPayloadLength", 4_096)]
    [InlineData("MaxAddressLength", 256)]
    [InlineData("MaxMetadataEntries", 64)]
    [InlineData("MaxMetadataValueLength", 512)]
    [InlineData("MaxExtensions", 32)]
    [InlineData("MaxExtensionValueLength", 1_024)]
    [InlineData("MaxPageSize", 1_000)]
    [InlineData("MaxTransactionConditions", 64)]
    [InlineData("MaxTransactionWrites", 64)]
    [InlineData("MaxBatchIncrements", 16)]
    [InlineData("MaxSemaphoreCapacity", 1_024)]
    [InlineData("MaxSequenceBlock", 65_536)]
    [InlineData("MaxSetMembers", 10_000)]
    public void Constants_Always_HaveTheirValues(string name, int expected)
    {
        var field = typeof(CoordinationLimits).GetField(name);

        Assert.NotNull(field);
        var actual = field.GetRawConstantValue();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constants_Always_AreExactlyTheListedOnes()
    {
        var count = typeof(CoordinationLimits).GetFields().Length;

        Assert.Equal(16, count);
    }

    #endregion
}
