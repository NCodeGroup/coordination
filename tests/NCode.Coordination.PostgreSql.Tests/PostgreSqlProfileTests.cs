// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlProfileTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_InvalidName_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlProfile(
                "PostgreSql",
                Guarantees.Durable,
                requiresSynchronousStandby: false
            )
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("'PostgreSql' is not a name", exception.Message);
    }

    [Fact]
    public void Constructor_NotADurabilityGuarantee_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlProfile(
                "postgresql",
                Guarantees.ServerClock,
                requiresSynchronousStandby: false
            )
        );

        Assert.Equal("durability", exception.ParamName);
        Assert.Contains("'expiry.server-clock' is not a durability guarantee", exception.Message);
    }

    #endregion

    #region Synchronous Property Tests

    [Fact]
    public void Synchronous_Always_RequiresASynchronousStandby()
    {
        var profile = PostgreSqlProfiles.Synchronous;

        Assert.Equal("postgresql-synchronous", profile.Name);
        Assert.Equal(Guarantees.Durable, profile.Durability);
        Assert.True(profile.RequiresSynchronousStandby);
        var text = profile.ToString();
        Assert.Equal("postgresql-synchronous", text);
    }

    #endregion

    #region Asynchronous Property Tests

    [Fact]
    public void Asynchronous_Always_DeclaresAsynchronousReplication()
    {
        var profile = PostgreSqlProfiles.Asynchronous;

        Assert.Equal("postgresql-asynchronous", profile.Name);
        Assert.Equal(Guarantees.ReplicatedAsynchronously, profile.Durability);
        Assert.False(profile.RequiresSynchronousStandby);
    }

    #endregion
}
