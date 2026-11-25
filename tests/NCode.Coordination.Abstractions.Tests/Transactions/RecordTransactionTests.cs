// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class RecordTransactionTests
{
    private static readonly Keyspace Prod = new("prod");

    private static CoordinationKey Key(string name, Keyspace? keyspace = null) =>
        new(keyspace ?? Prod, name);

    private static RecordWrite Put(string name, Keyspace? keyspace = null)
    {
        var key = Key(name, keyspace);
        var value = "v"u8.ToArray();
        return RecordWrite.Put(key, value, RecordLifetime.Persistent);
    }

    private static RecordCondition Absent(string name, Keyspace? keyspace = null)
    {
        var key = Key(name, keyspace);
        return RecordCondition.Absent(key);
    }

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
        var write = Put("b");

        var transaction = new RecordTransaction([], [write]);

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
        var write = Put("b");

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RecordTransaction(null!, [write])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAConditionIsNull_Throws()
    {
        var write = Put("b");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([null!], [write])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreSixtyFiveConditions_Throws()
    {
        var conditions = Enumerable.Range(0, 65).Select(i => Absent($"c{i}")).ToArray();
        var write = Put("b");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction(conditions, [write])
        );

        Assert.Equal("conditions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAConditionIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");
        var condition = Absent("a", other);
        var write = Put("b");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([condition], [write])
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
        var put = Put("a");
        var key = Key("a");
        var delete = RecordWrite.Delete(key);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [put, delete])
        );

        Assert.Equal("onSuccess", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAWriteIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");
        var first = Put("a");
        var second = Put("b", other);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [first, second])
        );

        Assert.Equal("onSuccess", exception.ParamName);
    }

    #endregion

    #region OnFailure Property Tests

    [Fact]
    public void OnFailure_ByDefault_IsEmpty()
    {
        var write = Put("a");

        var transaction = new RecordTransaction([], [write]);

        Assert.Empty(transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenWritesAreValid_KeepsThem()
    {
        var write = Put("a");
        var success = Put("b");

        var transaction = new RecordTransaction([], [success]) { OnFailure = [write] };

        Assert.Equal<RecordWrite>([write], transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenAKeyIsWrittenInBothBranches_Accepts()
    {
        var success = Put("a");
        var failure = Put("a");

        var transaction = new RecordTransaction([], [success]) { OnFailure = [failure] };

        Assert.Single(transaction.OnFailure);
    }

    [Fact]
    public void OnFailure_WhenArrayIsDefault_Throws()
    {
        var success = Put("a");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [success]) { OnFailure = default }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAWriteIsNull_Throws()
    {
        var success = Put("a");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [success]) { OnFailure = [null!] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenThereAreSixtyFiveWrites_Throws()
    {
        ImmutableArray<RecordWrite> writes = [.. Enumerable.Range(0, 65).Select(i => Put($"w{i}"))];
        var success = Put("a");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [success]) { OnFailure = writes }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAKeyIsWrittenTwice_Throws()
    {
        var success = Put("a");
        var first = Put("b");
        var second = Put("b");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [success]) { OnFailure = [first, second] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    [Fact]
    public void OnFailure_WhenAWriteIsInAnotherKeyspace_Throws()
    {
        var other = new Keyspace("test");
        var success = Put("a");
        var failure = Put("b", other);

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordTransaction([], [success]) { OnFailure = [failure] }
        );

        Assert.Equal("OnFailure", exception.ParamName);
    }

    #endregion
}
