// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class ReadLockGrantTests
{
    private static Keyspace Keyspace { get; } = new("locks");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheRegistrationIsUnderTheResource_KeepsTheValues()
    {
        var resource = Keyspace.CreateKey("resource");
        var registration = Keyspace.CreateKey("resource", "reader");
        var revision = new Revision(3);
        var fencingToken = new FencingToken(7);

        var grant = new ReadLockGrant(resource, registration, revision, fencingToken);

        Assert.Equal(resource, grant.Resource);
        Assert.Equal(registration, grant.Registration);
        Assert.Equal(revision, grant.Revision);
        Assert.Equal(fencingToken, grant.FencingToken);
    }

    [Fact]
    public void Constructor_WhenTheRegistrationIsTheResource_Throws()
    {
        var resource = Keyspace.CreateKey("resource");
        var revision = new Revision(3);
        var fencingToken = new FencingToken(7);

        var exception = Assert.Throws<ArgumentException>(() =>
            new ReadLockGrant(resource, resource, revision, fencingToken)
        );

        Assert.Equal("registration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheRegistrationIsUnderAnotherKey_Throws()
    {
        var resource = Keyspace.CreateKey("resource");
        var registration = Keyspace.CreateKey("other", "reader");
        var revision = new Revision(3);
        var fencingToken = new FencingToken(7);

        var exception = Assert.Throws<ArgumentException>(() =>
            new ReadLockGrant(resource, registration, revision, fencingToken)
        );

        Assert.Equal("registration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheResourceIsNull_Throws()
    {
        var registration = Keyspace.CreateKey("resource", "reader");
        var revision = new Revision(3);
        var fencingToken = new FencingToken(7);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ReadLockGrant(null!, registration, revision, fencingToken)
        );

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheRegistrationIsNull_Throws()
    {
        var resource = Keyspace.CreateKey("resource");
        var revision = new Revision(3);
        var fencingToken = new FencingToken(7);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ReadLockGrant(resource, null!, revision, fencingToken)
        );

        Assert.Equal("registration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheRevisionIsNull_Throws()
    {
        var resource = Keyspace.CreateKey("resource");
        var registration = Keyspace.CreateKey("resource", "reader");
        var fencingToken = new FencingToken(7);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ReadLockGrant(resource, registration, null!, fencingToken)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheFencingTokenIsNull_Throws()
    {
        var resource = Keyspace.CreateKey("resource");
        var registration = Keyspace.CreateKey("resource", "reader");
        var revision = new Revision(3);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ReadLockGrant(resource, registration, revision, null!)
        );

        Assert.Equal("fencingToken", exception.ParamName);
    }

    #endregion
}
