// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RespProfileTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_InvalidName_Throws()
    {
        var version = new Version(7, 2, 0);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RespProfile("Valkey", "valkey", "valkey_version", version, shardedChannels: true)
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("'Valkey' is not a name", exception.Message);
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var name = new string('a', 129);
        var version = new Version(7, 2, 0);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RespProfile(name, "valkey", "valkey_version", version, shardedChannels: true)
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("at most 128 characters, and this one has 129", exception.Message);
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        var version = new Version(7, 2, 0);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RespProfile(null!, "valkey", "valkey_version", version, shardedChannels: true)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_BlankServerName_Throws()
    {
        var version = new Version(7, 2, 0);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RespProfile("valkey", " ", "valkey_version", version, shardedChannels: true)
        );

        Assert.Equal("serverName", exception.ParamName);
    }

    [Fact]
    public void Constructor_BlankVersionField_Throws()
    {
        var version = new Version(7, 2, 0);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RespProfile("valkey", "valkey", " ", version, shardedChannels: true)
        );

        Assert.Equal("versionField", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullMinimumVersion_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RespProfile("valkey", "valkey", "valkey_version", null!, shardedChannels: true)
        );

        Assert.Equal("minimumVersion", exception.ParamName);
    }

    #endregion

    #region Valkey Property Tests

    [Fact]
    public void Valkey_Always_ExpectsValkeyServerName()
    {
        var profile = RespProfiles.Valkey;

        Assert.Equal("valkey", profile.Name);
        Assert.Equal("valkey", profile.ServerName);
        Assert.Equal("valkey_version", profile.VersionField);
        Assert.Equal(new Version(7, 2, 0), profile.MinimumVersion);
        Assert.True(profile.ShardedChannels);
        Assert.Equal("valkey", profile.ToString());
    }

    #endregion

    #region Garnet Property Tests

    [Fact]
    public void Garnet_Always_UsesPlainChannels()
    {
        var profile = RespProfiles.Garnet;

        Assert.Equal("garnet", profile.Name);
        Assert.Equal("garnet", profile.ServerName);
        Assert.Equal("garnet_version", profile.VersionField);
        Assert.Equal(new Version(2, 2, 13), profile.MinimumVersion);
        Assert.False(profile.ShardedChannels);
    }

    #endregion
}
