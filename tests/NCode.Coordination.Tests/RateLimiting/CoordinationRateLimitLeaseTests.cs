// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class CoordinationRateLimitLeaseTests
{
    #region Acquired Tests

    [Fact]
    public void Acquired_Always_IsAcquiredWithNoMetadata()
    {
        var lease = CoordinationRateLimitLease.Acquired;

        Assert.True(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    #endregion

    #region NotAcquired Tests

    [Fact]
    public void NotAcquired_Always_IsNotAcquiredWithNoMetadata()
    {
        var lease = CoordinationRateLimitLease.NotAcquired;

        Assert.False(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    #endregion

    #region Denied Tests

    [Fact]
    public void Denied_WhenRetryAfterIsPositive_ReportsItAsMetadata()
    {
        var lease = CoordinationRateLimitLease.Denied(TimeSpan.FromSeconds(5));

        Assert.False(lease.IsAcquired);
        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(5), retryAfter);
        Assert.Equal([MetadataName.RetryAfter.Name], lease.MetadataNames);
    }

    [Fact]
    public void Denied_WhenRetryAfterIsNegative_ReportsZero()
    {
        var lease = CoordinationRateLimitLease.Denied(TimeSpan.FromSeconds(-1));

        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    #endregion

    #region TryGetMetadata Tests

    [Fact]
    public void TryGetMetadata_WhenTheNameIsUnknown_ReturnsFalse()
    {
        var lease = CoordinationRateLimitLease.Denied(TimeSpan.FromSeconds(5));

        Assert.False(lease.TryGetMetadata("other", out var metadata));
        Assert.Null(metadata);
    }

    #endregion
}
