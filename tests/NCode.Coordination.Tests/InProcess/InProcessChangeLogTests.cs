// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessChangeLogTests
{
    private static InProcessLoggedChange CreateChange(long revision, bool isDelete = false) =>
        new(InProcessTesting.Keyspace.CreateKey("a"), new Revision(revision), isDelete);

    #region Covers Tests

    [Fact]
    public void Covers_WhenNothingWasDropped_CoversRevisionOne()
    {
        var log = new InProcessChangeLog();

        log.Append(CreateChange(2));

        Assert.Equal(1, log.Floor);
        Assert.True(log.Covers(new Revision(1)));
    }

    #endregion

    #region Append Tests

    [Fact]
    public void Append_WhenTheLogIsFull_DropsTheOldestAndRaisesTheFloor()
    {
        var log = new InProcessChangeLog();

        for (var index = 0; index <= InProcessChangeLog.Capacity; index++)
        {
            log.Append(CreateChange(index + 2));
        }

        Assert.Equal(2, log.Floor);
        Assert.False(log.Covers(new Revision(1)));
        Assert.True(log.Covers(new Revision(2)));
        Assert.Equal(InProcessChangeLog.Capacity, log.After(new Revision(1)).Count);
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

        var changes = log.After(new Revision(2));

        Assert.Equal([third, fourth], changes);
    }

    #endregion
}
