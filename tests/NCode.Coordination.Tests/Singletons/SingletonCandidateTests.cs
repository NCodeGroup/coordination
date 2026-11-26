// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SingletonCandidateTests
{
    private static SessionId CreateSession()
    {
        var keyspace = new Keyspace("singletons");
        return new SessionId(keyspace, "session-1");
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheSessionIsValid_KeepsTheSession()
    {
        var session = CreateSession();

        var candidate = new SingletonCandidate(session);

        Assert.Same(session, candidate.Session);
    }

    [Fact]
    public void Constructor_WhenTheSessionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SingletonCandidate(null!));

        Assert.Equal("session", exception.ParamName);
    }

    #endregion

    #region Value Property Tests

    [Fact]
    public void Value_ByDefault_IsEmpty()
    {
        var session = CreateSession();

        var candidate = new SingletonCandidate(session);

        Assert.True(candidate.Value.IsEmpty);
    }

    [Fact]
    public void Value_WhenLongerThanTheLimit_Throws()
    {
        var session = CreateSession();
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            new SingletonCandidate(session) { Value = value }
        );

        Assert.Equal("Value", exception.ParamName);
    }

    [Fact]
    public void Value_WhenExactlyTheLimit_KeepsTheValue()
    {
        var session = CreateSession();
        var value = new byte[CoordinationLimits.MaxValueLength];

        var candidate = new SingletonCandidate(session) { Value = value };

        Assert.Equal(CoordinationLimits.MaxValueLength, candidate.Value.Length);
    }

    [Fact]
    public void Value_WhenTheSourceArrayChanges_KeepsItsOwnCopy()
    {
        var session = CreateSession();
        byte[] source = [1, 2, 3];
        var candidate = new SingletonCandidate(session) { Value = source };

        source[0] = 9;
        var copy = candidate.Value.ToArray();

        Assert.Equal<byte>([1, 2, 3], copy);
    }

    #endregion
}
