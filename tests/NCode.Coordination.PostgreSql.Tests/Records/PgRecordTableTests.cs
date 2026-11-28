// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordTableTests
{
    #region DecodeKey Tests

    [Fact]
    public void DecodeKey_Malformed_ThrowsUnavailable()
    {
        var keyspace = new Keyspace("prod", "records");

        var failure = Assert.Throws<CoordinationException>(() =>
            PgRecordTable.DecodeKey(keyspace, "x%zz", 1)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
        Assert.False(failure.OutcomeUnknown);
    }

    [Fact]
    public void DecodeKey_DepthPastTheSegments_ThrowsUnavailable()
    {
        var keyspace = new Keyspace("prod", "records");

        var failure = Assert.Throws<CoordinationException>(() =>
            PgRecordTable.DecodeKey(keyspace, "tabc", 5)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
        Assert.False(failure.OutcomeUnknown);
    }

    #endregion
}
