// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class RecordTransactionTests
{
    private static readonly Keyspace Prod = new("prod");

    private static CoordinationKey Key(string name, Keyspace? keyspace = null) =>
        new(keyspace ?? Prod, name);

    private static RecordWrite Put(string name, Keyspace? keyspace = null) =>
        RecordWrite.Put(Key(name, keyspace), "v"u8.ToArray(), RecordLifetime.Persistent);

    private static RecordCondition Absent(string name, Keyspace? keyspace = null) =>
        RecordCondition.Absent(Key(name, keyspace));

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsListsAndKeyspace()
    {
        var condition = Absent("a");
        var write = Put("b");

        var transaction = new RecordTransaction([condition], [write]);

        Assert.Equal<RecordCondition>([condition], transaction.Conditions);
        Assert.Equal<RecordWrite>([write], transaction.OnSuccess);
        Assert.Equal(Prod, transaction.Keyspace);
    }

    [Fact]
    public void Constructor_WhenThereAreNoConditions_Accepts()
    {
        var transaction = new RecordTransaction([], [Put("b")]);

        Assert.Empty(transaction.Conditions);
    }

    [Fact]
    public void Constructor_WhenThereAreSixtyFourConditionsAndWrites_Accepts()
    {
        var conditions = Enumerable.Range(0, 64).Select(i => Absent($"c{i}")).ToArray();
        var writes = Enumerable.Range(0, 64).Select(i => Put($"w{i}")).ToArray();

        var transaction = new RecordTransaction(conditions, writes);

        Assert.Equal(64, transaction.Conditions.Length);
        Assert.Equal(64, transaction.OnSuccess.Length);
    }

    [Fact]
    public void Constructor_WhenConditionsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RecordTransaction(null!, [Put("b")])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAConditionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([null!], [Put("b")])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreSixtyFiveConditions_Throws()
    {
        var conditions = Enumerable.Range(0, 65).Select(i => Absent($"c{i}")).ToArray();

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction(conditions, [Put("b")])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAConditionIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([Absent("a", other)], [Put("b")])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenWritesAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RecordTransaction([], null!)
        );

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAWriteIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RecordTransaction([], [null!]));

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreNoWrites_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RecordTransaction([], []));

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreSixtyFiveWrites_Throws()
    {
        var writes = Enumerable.Range(0, 65).Select(i => Put($"w{i}")).ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new RecordTransaction([], writes));

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAKeyIsWrittenTwice_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a"), RecordWrite.Delete(Key("a"))])
        );

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAWriteIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a"), Put("b", other)])
        );

        Assert.Equal("onSuccess", exception.ParamName);
    }

    #endregion

    #region OnFailure Property Tests

    [Fact]
    public void OnFailure_ByDefault_IsEmpty()
    {
        var transaction = new RecordTransaction([], [Put("a")]);

        Assert.Empty(transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenWritesAreValid_KeepsThem()
    {
        var write = Put("a");

        var transaction = new RecordTransaction([], [Put("b")]) { OnFailure = [write] };

        Assert.Equal<RecordWrite>([write], transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenAKeyIsWrittenInBothBranches_Accepts()
    {
        var transaction = new RecordTransaction([], [Put("a")]) { OnFailure = [Put("a")] };

        Assert.Single(transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenArrayIsDefault_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a")]) { OnFailure = default }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAWriteIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a")]) { OnFailure = [null!] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenThereAreSixtyFiveWrites_Throws()
    {
        ImmutableArray<RecordWrite> writes = [.. Enumerable.Range(0, 65).Select(i => Put($"w{i}"))];

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a")]) { OnFailure = writes }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAKeyIsWrittenTwice_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a")]) { OnFailure = [Put("b"), Put("b")] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAWriteIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [Put("a")]) { OnFailure = [Put("b", other)] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    #endregion
}
