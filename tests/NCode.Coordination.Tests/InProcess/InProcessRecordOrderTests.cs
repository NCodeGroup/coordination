// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessRecordOrderTests
{
    private static CoordinationKey CreateKey(string segment)
    {
        return InProcessTesting.Keyspace.CreateKey(segment);
    }

    #region After Tests

    [Fact]
    public void After_WhenSequencesAreAdded_ReturnsTheirKeysInOrder()
    {
        var order = new InProcessRecordOrder();
        var first = CreateKey("a");
        var second = CreateKey("b");
        var third = CreateKey("c");

        order.Add(3, third);
        order.Add(1, first);
        order.Add(2, second);

        var keys = order.After(0).ToArray();

        Assert.Equal([first, second, third], keys);
    }

    [Fact]
    public void After_WhenASequenceIsRemoved_SkipsIt()
    {
        var order = new InProcessRecordOrder();
        var first = CreateKey("a");
        var second = CreateKey("b");
        var third = CreateKey("c");

        order.Add(1, first);
        order.Add(2, second);
        order.Add(3, third);
        order.Remove(2);

        var keys = order.After(0).ToArray();

        Assert.Equal([first, third], keys);
    }

    [Fact]
    public void After_WhenTheSequenceIsInTheMiddle_StartsAfterIt()
    {
        var order = new InProcessRecordOrder();
        var first = CreateKey("a");
        var second = CreateKey("b");
        var third = CreateKey("c");

        order.Add(1, first);
        order.Add(2, second);
        order.Add(3, third);

        var keys = order.After(1).ToArray();

        Assert.Equal([second, third], keys);
    }

    [Fact]
    public void After_WhenNothingFollows_ReturnsEmpty()
    {
        var order = new InProcessRecordOrder();
        var first = CreateKey("a");

        order.Add(1, first);

        var keys = order.After(1).ToArray();

        Assert.Empty(keys);
    }

    #endregion
}
