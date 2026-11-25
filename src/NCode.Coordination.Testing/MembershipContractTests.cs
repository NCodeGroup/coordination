// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the membership primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class MembershipContractTests : ProviderContractTests
{
    /// <summary>
    /// The address that the members of the suite carry.
    /// </summary>
    private const string Address = "10.0.0.4:5000";

    /// <summary>
    /// The address that a member carries when its session joins again with a replacement.
    /// </summary>
    private const string ReplacementAddress = "10.0.0.5:5001";

    /// <summary>
    /// Creates a member of the suite.
    /// </summary>
    /// <param name="session">The session that keeps the member alive.</param>
    /// <param name="nodeId">The identity of the node.</param>
    /// <returns>The member.</returns>
    private static Member CreateMember(SessionId session, string nodeId) =>
        new(session, nodeId, Address);

    /// <summary>
    /// Gets the provider's membership service, and skips the test when the provider does not support membership.
    /// </summary>
    private IMembershipService Members => GetService<IMembershipService>(Primitives.Membership);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    #region JoinAsync Tests

    /// <summary>
    /// Checks that a member whose session is live is listed with its node id, address, session and metadata.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenSessionIsLive_ListsTheMember()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = new Member(session, "a", Address)
        {
            Metadata = ImmutableDictionary<string, string>.Empty.Add("conformance.zone", "a"),
        };

        var status = await Members.JoinAsync(group, member, null, TestCancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, status);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        var listed = Assert.Single(view.Members);
        Assert.Equal(group, view.Group);
        Assert.Equal("a", listed.NodeId);
        Assert.Equal(Address, listed.Address);
        Assert.Equal(session, listed.Session);
        Assert.Equal("a", listed.Metadata["conformance.zone"]);
    }

    /// <summary>
    /// Checks that joining with a node id that another live session holds returns a node id taken result and adds no
    /// member.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenNodeIdIsHeldByAnotherSession_ReturnsNodeIdTaken()
    {
        var group = CreateKey("group");
        var first = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var second = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var holder = CreateMember(first, "a");
        var other = CreateMember(second, "a");
        await Members.JoinAsync(group, holder, null, TestCancellationToken);

        var status = await Members.JoinAsync(group, other, null, TestCancellationToken);

        Assert.Equal(MembershipJoinStatus.NodeIdTaken, status);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        var listed = Assert.Single(view.Members);
        Assert.Equal(first, listed.Session);
    }

    /// <summary>
    /// Checks that a session that joins again replaces its entry instead of adding a second one.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenTheSessionJoinsAgain_ReplacesItsEntry()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var first = CreateMember(session, "a");
        var second = new Member(session, "b", ReplacementAddress);
        await Members.JoinAsync(group, first, null, TestCancellationToken);

        var status = await Members.JoinAsync(group, second, null, TestCancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, status);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        var listed = Assert.Single(view.Members);
        Assert.Equal("b", listed.NodeId);
        Assert.Equal(ReplacementAddress, listed.Address);
    }

    /// <summary>
    /// Checks that joining with a session that has ended returns a session ended result and adds no member.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenSessionHasEnded_ReturnsSessionEnded()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");

        var status = await Members.JoinAsync(group, member, null, TestCancellationToken);

        Assert.Equal(MembershipJoinStatus.SessionEnded, status);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        Assert.Empty(view.Members);
    }

    /// <summary>
    /// Checks that a member leaves the group when its session is closed.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenTheSessionCloses_RemovesTheMember()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");
        await Members.JoinAsync(group, member, null, TestCancellationToken);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        Assert.Empty(view.Members);
    }

    /// <summary>
    /// Checks that a member leaves the group when its session's time to live passes.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenTheSessionExpires_RemovesTheMember()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            ShortTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");
        await Members.JoinAsync(group, member, null, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var before = await Members.ReadAsync(group, null, TestCancellationToken);
        Assert.Single(before.Members);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await Members.ReadAsync(group, null, TestCancellationToken);
        Assert.Empty(after.Members);
    }

    /// <summary>
    /// Checks that joining with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task JoinAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Members.JoinAsync(group, member, UnknownExtension, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region LeaveAsync Tests

    /// <summary>
    /// Checks that leaving a group removes the session's entry and keeps the other members.
    /// </summary>
    [Fact]
    public async Task LeaveAsync_WhenSessionIsAMember_RemovesItsEntry()
    {
        var group = CreateKey("group");
        var leaving = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var staying = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var leavingMember = CreateMember(leaving, "a");
        var stayingMember = CreateMember(staying, "b");
        await Members.JoinAsync(group, leavingMember, null, TestCancellationToken);
        await Members.JoinAsync(group, stayingMember, null, TestCancellationToken);

        await Members.LeaveAsync(group, leaving, null, TestCancellationToken);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        var listed = Assert.Single(view.Members);
        Assert.Equal("b", listed.NodeId);
    }

    /// <summary>
    /// Checks that leaving a group that the session is not in succeeds.
    /// </summary>
    [Fact]
    public async Task LeaveAsync_WhenSessionIsNotAMember_Succeeds()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );

        await Members.LeaveAsync(group, session, null, TestCancellationToken);

        var view = await Members.ReadAsync(group, null, TestCancellationToken);
        Assert.Empty(view.Members);
    }

    #endregion

    #region ReadAsync Tests

    /// <summary>
    /// Checks that reading a group that nobody joined returns no members.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenGroupIsEmpty_ReturnsNoMembers()
    {
        var group = CreateKey("group");

        var view = await Members.ReadAsync(group, null, TestCancellationToken);

        Assert.Equal(group, view.Group);
        Assert.Empty(view.Members);
    }

    /// <summary>
    /// Checks that a change of the members gives the view a later revision.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenMembershipChanges_GivesALaterRevision()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");
        var before = await Members.ReadAsync(group, null, TestCancellationToken);
        await Members.JoinAsync(group, member, null, TestCancellationToken);

        var after = await Members.ReadAsync(group, null, TestCancellationToken);

        Assert.True(after.Revision > before.Revision);
    }

    /// <summary>
    /// Checks that reading with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var group = CreateKey("group");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Members.ReadAsync(group, null, canceledToken)
        );
    }

    #endregion

    #region WatchAsync Tests

    /// <summary>
    /// Checks that a watch yields the current view of the group first.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenStarted_YieldsTheCurrentViewFirst()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");
        await Members.JoinAsync(group, member, null, TestCancellationToken);
        var current = await Members.ReadAsync(group, null, TestCancellationToken);

        var watch = Members.WatchAsync(group, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch);
        var first = await ContractStreams.NextAsync(stream);

        Assert.Equal(group, first.Group);
        Assert.Equal(current.Revision, first.Revision);
        var listed = Assert.Single(first.Members);
        Assert.Equal("a", listed.NodeId);
    }

    /// <summary>
    /// Checks that a watch yields a view with a member that joins after the watch started, at a later revision.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenAMemberJoins_YieldsAViewWithIt()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "b");
        var watch = Members.WatchAsync(group, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch);
        var first = await ContractStreams.NextAsync(stream);
        await Members.JoinAsync(group, member, null, TestCancellationToken);

        var view = await ContractStreams.NextMatchingAsync(stream, candidate =>
            candidate.Members.Any(listed => listed.NodeId == "b")
        );

        Assert.True(view.Revision > first.Revision);
    }

    /// <summary>
    /// Checks that a watch yields a view without a member whose session ends.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenAMembersSessionEnds_YieldsAViewWithoutIt()
    {
        var group = CreateKey("group");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var member = CreateMember(session, "a");
        await Members.JoinAsync(group, member, null, TestCancellationToken);
        var watch = Members.WatchAsync(group, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch);
        var first = await ContractStreams.NextAsync(stream);
        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var view = await ContractStreams.NextMatchingAsync(stream, candidate =>
            candidate.Members.IsEmpty
        );

        Assert.Single(first.Members);
        Assert.True(view.Revision > first.Revision);
    }

    #endregion
}
