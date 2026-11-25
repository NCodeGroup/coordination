// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessChangeLogTests
{
    private static InProcessLoggedChange CreateChange(long revision, bool isDelete = false)
    {
        var key = InProcessTesting.Keyspace.CreateKey("a");
        var changeRevision = new Revision(revision);

        return new InProcessLoggedChange(key, changeRevision, isDelete);
    }

    #region Covers Tests

    [Fact]
    public void Covers_WhenNothingWasDropped_CoversRevisionOne()
    {
        var log = new InProcessChangeLog();

        var change = CreateChange(2);
        log.Append(change);

        var first = new Revision(1);

        Assert.Equal(1, log.Floor);
        Assert.True(log.Covers(first));
    }

    #endregion

    #region Append Tests

    [Fact]
    public void Append_WhenTheLogIsFull_DropsTheOldestAndRaisesTheFloor()
    {
        var log = new InProcessChangeLog();

        for (var index = 0; index <= InProcessChangeLog.Capacity; index++)
        {
            var change = CreateChange(index + 2);
            log.Append(change);
        }

        var first = new Revision(1);
        var second = new Revision(2);
        var changes = log.After(first);

        Assert.Equal(2, log.Floor);
        Assert.False(log.Covers(first));
        Assert.True(log.Covers(second));
        Assert.Equal(InProcessChangeLog.Capacity, changes.Count);
    }

    #endregion

    #region After Tests

    [Fact]
    public void After_WhenGivenARevision_ReturnsTheLaterChangesInOrder()
    {
        var log = new InProcessChangeLog();
        var second = CreateChange(2);
        var third = CreateChange(3, isDelete: true);
        var fourth = CreateChange(4);
        log.Append(second);
        log.Append(third);
        log.Append(fourth);

        var after = new Revision(2);

        var changes = log.After(after);

        Assert.Equal([third, fourth], changes);
    }

    #endregion
}
