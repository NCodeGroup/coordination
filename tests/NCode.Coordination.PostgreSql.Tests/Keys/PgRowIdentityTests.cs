// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Keys.Tests;

public sealed class PgRowIdentityTests
{
    #region Equals Tests

    [Fact]
    public void Equals_EqualBytesInOtherArrays_IsTrueWithEqualHashCodes()
    {
        var first = new PgRowIdentity([1, 2, 3], 2);
        var second = new PgRowIdentity([1, 2, 3], 2);

        var equal = first.Equals(second);
        var firstHash = first.GetHashCode();
        var secondHash = second.GetHashCode();

        Assert.True(equal);
        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void Equals_OtherDepth_IsFalse()
    {
        var first = new PgRowIdentity([1, 2, 3], 2);
        var second = new PgRowIdentity([1, 2, 3], 3);

        var equal = first.Equals(second);

        Assert.False(equal);
    }

    [Fact]
    public void Equals_OtherByte_IsFalse()
    {
        var first = new PgRowIdentity([1, 2, 3], 2);
        var second = new PgRowIdentity([1, 2, 4], 2);

        var equal = first.Equals(second);

        Assert.False(equal);
    }

    #endregion
}
