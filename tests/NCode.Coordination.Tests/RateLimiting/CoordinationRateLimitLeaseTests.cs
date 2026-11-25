// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class CoordinationRateLimitLeaseTests
{
    #region Acquired Property Tests

    [Fact]
    public void Acquired_Always_IsAcquiredWithNoMetadata()
    {
        var lease = CoordinationRateLimitLease.Acquired;

        var found = lease.TryGetMetadata(MetadataName.RetryAfter, out _);
        Assert.True(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(found);
    }

    #endregion

    #region NotAcquired Property Tests

    [Fact]
    public void NotAcquired_Always_IsNotAcquiredWithNoMetadata()
    {
        var lease = CoordinationRateLimitLease.NotAcquired;

        var found = lease.TryGetMetadata(MetadataName.RetryAfter, out _);
        Assert.False(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(found);
    }

    #endregion

    #region Denied Tests

    [Fact]
    public void Denied_WhenRetryAfterIsPositive_ReportsItAsMetadata()
    {
        var expected = TimeSpan.FromSeconds(5);
        var lease = CoordinationRateLimitLease.Denied(expected);

        var found = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        Assert.False(lease.IsAcquired);
        Assert.True(found);
        Assert.Equal(expected, retryAfter);
        Assert.Equal([MetadataName.RetryAfter.Name], lease.MetadataNames);
    }

    [Fact]
    public void Denied_WhenRetryAfterIsNegative_ReportsZero()
    {
        var negative = TimeSpan.FromSeconds(-1);
        var lease = CoordinationRateLimitLease.Denied(negative);

        var found = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        Assert.True(found);
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    #endregion

    #region TryGetMetadata Tests

    [Fact]
    public void TryGetMetadata_WhenTheNameIsUnknown_ReturnsFalse()
    {
        var retryAfter = TimeSpan.FromSeconds(5);
        var lease = CoordinationRateLimitLease.Denied(retryAfter);

        var found = lease.TryGetMetadata("other", out var metadata);
        Assert.False(found);
        Assert.Null(metadata);
    }

    #endregion
}
