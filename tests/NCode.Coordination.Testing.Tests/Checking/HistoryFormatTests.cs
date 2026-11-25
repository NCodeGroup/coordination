// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class HistoryFormatTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Key { get; } = new(Space, "k");

    private static LeaseTerms Terms { get; } =
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    private static OwnerToken Owner { get; } = new("owner");

    #region Describe Tests

    [Fact]
    public void Describe_WhenCallReturned_ShowsClientStampsInputAndOutput()
    {
        var operation = HistoryTesting.Operation(2, "p", "read", 3, 7, 5);

        var text = HistoryFormat.Describe(operation);

        Assert.Equal("client 2 [3, 7] read -> 5", text);
    }

    [Fact]
    public void Describe_WhenCallFailed_ShowsFailed()
    {
        var operation = new HistoryOperation(
            1,
            "p",
            "write",
            4,
            6,
            HistoryOutcome.Failed,
            new InvalidOperationException()
        );

        var text = HistoryFormat.Describe(operation);

        Assert.Equal("client 1 [4, 6] write -> failed", text);
    }

    [Fact]
    public void Describe_WhenCallIsIndeterminate_ShowsNeverAndIndeterminate()
    {
        var operation = HistoryTesting.Indeterminate("write");

        var text = HistoryFormat.Describe(operation);

        Assert.Equal("client 0 [1, never] write -> indeterminate", text);
    }

    [Fact]
    public void Describe_WhenCallReturnedNothing_ShowsNothing()
    {
        var operation = HistoryTesting.Returned("write", null);

        var text = HistoryFormat.Describe(operation);

        Assert.Equal("client 0 [1, 2] write -> nothing", text);
    }

    #endregion

    #region DescribeValue Tests

    [Fact]
    public void DescribeValue_WhenNull_ReturnsNothing()
    {
        var text = HistoryFormat.DescribeValue(null);

        Assert.Equal("nothing", text);
    }

    [Fact]
    public void DescribeValue_WhenStoredRecord_ReturnsValueAndRevision()
    {
        var record = new StoredRecord(Key, HistoryValues.Encode("hello"), new Revision(9));

        var text = HistoryFormat.DescribeValue(record);

        Assert.Equal("'hello' at 9", text);
    }

    [Fact]
    public void DescribeValue_WhenRecordWriteResult_ReturnsStatusAndRevision()
    {
        var result = RecordWriteResult.Written(new Revision(5));

        var text = HistoryFormat.DescribeValue(result);

        Assert.Equal("Written 5", text);
    }

    [Fact]
    public void DescribeValue_WhenTransactionResult_ReturnsStatusAndRevision()
    {
        var result = new TransactionResult(TransactionStatus.Succeeded, new Revision(7));

        var text = HistoryFormat.DescribeValue(result);

        Assert.Equal("Succeeded 7", text);
    }

    [Fact]
    public void DescribeValue_WhenLeaseAcquired_ReturnsStatusAndToken()
    {
        var result = LeaseAcquireResult.Acquired(
            new LeaseGrant(Key, new FencingToken(3), Owner, Terms)
        );

        var text = HistoryFormat.DescribeValue(result);

        Assert.Equal("Acquired 3", text);
    }

    [Fact]
    public void DescribeValue_WhenLeaseHeld_ReturnsStatusWithoutToken()
    {
        var text = HistoryFormat.DescribeValue(LeaseAcquireResult.Held);

        Assert.Equal("Held ", text);
    }

    [Fact]
    public void DescribeValue_WhenSemaphoreAcquired_ReturnsStatusSlotAndToken()
    {
        var result = SemaphoreAcquireResult.Acquired(
            new SemaphoreGrant(Key, 2, new FencingToken(4), Owner, Terms)
        );

        var text = HistoryFormat.DescribeValue(result);

        Assert.Equal("Acquired slot 2 4", text);
    }

    [Fact]
    public void DescribeValue_WhenSemaphoreFull_ReturnsStatusWithoutSlot()
    {
        var text = HistoryFormat.DescribeValue(SemaphoreAcquireResult.Full);

        Assert.Equal("Full slot  ", text);
    }

    [Fact]
    public void DescribeValue_WhenElectionElected_ReturnsStatusAndTerm()
    {
        var leader = new Leader(Key, new FencingToken(6), HistoryValues.Encode("alpha"));
        var result = ElectionResult.Elected(new Leadership(leader, Owner, Terms));

        var text = HistoryFormat.DescribeValue(result);

        Assert.Equal("Elected 6", text);
    }

    [Fact]
    public void DescribeValue_WhenElectionLed_ReturnsStatusWithoutTerm()
    {
        var text = HistoryFormat.DescribeValue(ElectionResult.Led);

        Assert.Equal("Led ", text);
    }

    [Fact]
    public void DescribeValue_WhenElectionViewHasNoLeader_ReturnsNoLeader()
    {
        var view = new ElectionView(Key, null);

        var text = HistoryFormat.DescribeValue(view);

        Assert.Equal("no leader", text);
    }

    [Fact]
    public void DescribeValue_WhenElectionViewHasLeader_ReturnsTermAndValue()
    {
        var leader = new Leader(Key, new FencingToken(8), HistoryValues.Encode("alpha"));
        var view = new ElectionView(Key, leader);

        var text = HistoryFormat.DescribeValue(view);

        Assert.Equal("leader 8 'alpha'", text);
    }

    [Fact]
    public void DescribeValue_WhenMembershipView_ReturnsSessionsJoined()
    {
        var first = new SessionId(Space, "s1");
        var second = new SessionId(Space, "s2");
        var view = new MembershipView(
            Key,
            new Revision(1),
            new Member(first, "a", "address-a"),
            new Member(second, "b", "address-b")
        );

        var text = HistoryFormat.DescribeValue(view);

        Assert.Equal($"members {first}, {second}", text);
    }

    [Fact]
    public void DescribeValue_WhenSessionGrant_ReturnsItsId()
    {
        var id = new SessionId(Space, "s1");
        var grant = new SessionGrant(id, new TimeToLive(TimeSpan.FromSeconds(30)));

        var text = HistoryFormat.DescribeValue(grant);

        Assert.Equal(id.ToString(), text);
    }

    [Fact]
    public void DescribeValue_WhenKeepAliveResult_ReturnsStatus()
    {
        var text = HistoryFormat.DescribeValue(KeepAliveResult.Ended);

        Assert.Equal("Ended", text);
    }

    [Fact]
    public void DescribeValue_WhenSequenceBlock_ReturnsFirstAndLast()
    {
        var text = HistoryFormat.DescribeValue(new SequenceBlock(10, 5));

        Assert.Equal("10..14", text);
    }

    [Fact]
    public void DescribeValue_WhenKeySegments_ReturnsThemJoined()
    {
        ImmutableArray<KeySegment> segments = ["alpha", 7L];

        var text = HistoryFormat.DescribeValue(segments);

        Assert.Equal("alpha, 7", text);
    }

    [Fact]
    public void DescribeValue_WhenOtherValue_ReturnsItsInvariantText()
    {
        var text = HistoryFormat.DescribeValue(1234.5);

        Assert.Equal("1234.5", text);
    }

    #endregion
}
