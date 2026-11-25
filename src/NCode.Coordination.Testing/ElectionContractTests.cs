// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the election primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class ElectionContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's election service, and skips the test when the provider does not support elections.
    /// </summary>
    private IElectionService Elections => GetService<IElectionService>(Primitives.Election);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    /// <summary>
    /// Becomes the leader of a role for a time to live and returns the leadership, failing the test when the caller
    /// is not elected.
    /// </summary>
    /// <param name="role">The role to lead.</param>
    /// <param name="value">The value followers read.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leadership.</returns>
    private async ValueTask<Leadership> LeadAsync(
        CoordinationKey role,
        byte[] value,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var result = await TryLeadAsync(role, value, timeToLive, cancellationToken);

        Assert.True(result.IsElected);
        return result.Leadership;
    }

    /// <summary>
    /// Tries to become the leader of a role for a time to live and returns the result.
    /// </summary>
    /// <param name="role">The role to lead.</param>
    /// <param name="value">The value followers read.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The result of the attempt.</returns>
    private ValueTask<ElectionResult> TryLeadAsync(
        CoordinationKey role,
        byte[] value,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        return Elections.TryLeadAsync(role, value, terms, null, cancellationToken);
    }

    #region TryLeadAsync Tests

    /// <summary>
    /// Checks that trying to lead a role that has no leader elects the caller, and that followers read its value.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenRoleHasNoLeader_ElectsTheCaller()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();

        var result = await TryLeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        Assert.Equal(ElectionStatus.Elected, result.Status);
        var leadership = Assert.IsType<Leadership>(result.Leadership);
        Assert.Equal(role, leadership.Leader.Role);
        var leadershipValue = leadership.Leader.Value.ToArray();
        Assert.Equal(value, leadershipValue);
        Assert.False(leadership.Terms.IsBoundToSession);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        var leader = Assert.IsType<Leader>(view.Leader);
        var leaderValue = leader.Value.ToArray();
        Assert.Equal(leadership.Leader.Term, leader.Term);
        Assert.Equal(value, leaderValue);
    }

    /// <summary>
    /// Checks that trying to lead a role that another node leads returns a led result with no leadership and keeps
    /// the leader.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenAnotherLeads_ReturnsLed()
    {
        var role = CreateKey("role");
        var nodeA = "node-a"u8.ToArray();
        var nodeB = "node-b"u8.ToArray();
        var leadership = await LeadAsync(role, nodeA, LongTimeToLive, TestCancellationToken);

        var result = await TryLeadAsync(role, nodeB, LongTimeToLive, TestCancellationToken);

        Assert.Equal(ElectionStatus.Led, result.Status);
        Assert.Null(result.Leadership);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        var leader = Assert.IsType<Leader>(view.Leader);
        var leaderValue = leader.Value.ToArray();
        Assert.Equal(leadership.Leader.Term, leader.Term);
        Assert.Equal(nodeA, leaderValue);
    }

    /// <summary>
    /// Checks that trying to lead again with the session of the leadership already held returns that leadership.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenTheSameSessionLeadsAgain_ReturnsItsLeadership()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var terms = LeaseTerms.ForSession(session);
        var first = await Elections.TryLeadAsync(role, value, terms, null, TestCancellationToken);

        var second = await Elections.TryLeadAsync(role, value, terms, null, TestCancellationToken);

        Assert.True(first.IsElected);
        Assert.True(second.IsElected);
        Assert.Equal(first.Leadership.Owner, second.Leadership.Owner);
        Assert.Equal(first.Leadership.Leader.Term, second.Leadership.Leader.Term);
    }

    /// <summary>
    /// Checks that trying to lead with a session that has ended returns a session ended result and elects nobody.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenSessionHasEnded_ReturnsSessionEnded()
    {
        var role = CreateKey("role");
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );

        var value = "node-a"u8.ToArray();
        var terms = LeaseTerms.ForSession(session);

        var result = await Elections.TryLeadAsync(role, value, terms, null, TestCancellationToken);

        Assert.Equal(ElectionStatus.SessionEnded, result.Status);
        Assert.Null(result.Leadership);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(view.Leader);
    }

    /// <summary>
    /// Checks that trying to lead with a value longer than the limit throws and elects nobody.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenValueIsTooLong_ThrowsArgumentException()
    {
        var role = CreateKey("role");
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await TryLeadAsync(role, value, LongTimeToLive, TestCancellationToken)
        );

        Assert.Equal("value", exception.ParamName);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(view.Leader);
    }

    /// <summary>
    /// Checks that trying to lead with a value of exactly the limit elects the caller and that followers read a value
    /// of that length.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenValueIsAtTheLimit_Leads()
    {
        var role = CreateKey("role");
        var value = new byte[CoordinationLimits.MaxValueLength];

        var result = await TryLeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        Assert.Equal(ElectionStatus.Elected, result.Status);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        var leader = Assert.IsType<Leader>(view.Leader);
        Assert.Equal(CoordinationLimits.MaxValueLength, leader.Value.Length);
    }

    /// <summary>
    /// Checks that a leader elected after a resignation gets a greater term than the earlier leader's.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_AfterResign_GivesAGreaterTerm()
    {
        var role = CreateKey("role");
        var nodeA = "node-a"u8.ToArray();
        var nodeB = "node-b"u8.ToArray();
        var earlier = await LeadAsync(role, nodeA, LongTimeToLive, TestCancellationToken);
        await Elections.ResignAsync(earlier, null, TestCancellationToken);

        var later = await LeadAsync(role, nodeB, LongTimeToLive, TestCancellationToken);

        Assert.True(later.Leader.Term > earlier.Leader.Term);
    }

    /// <summary>
    /// Checks that a leadership ends when its time to live passes, and that it lasts until then.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenTheLeadershipExpires_VacatesTheRole()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        await LeadAsync(role, value, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var before = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.NotNull(before.Leader);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(after.Leader);
    }

    /// <summary>
    /// Checks that a leadership ends when the session it is bound to closes.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenTheSessionCloses_VacatesTheRole()
    {
        var role = CreateKey("role");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var value = "node-a"u8.ToArray();
        var terms = LeaseTerms.ForSession(session);
        var elected = await Elections.TryLeadAsync(role, value, terms, null, TestCancellationToken);
        Assert.True(elected.IsElected);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(view.Leader);
    }

    /// <summary>
    /// Checks that trying to lead with an option extension that the provider does not know throws an unsupported
    /// error.
    /// </summary>
    [Fact]
    public async Task TryLeadAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var terms = LeaseTerms.ForTimeToLive(LongTimeToLive);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Elections.TryLeadAsync(
                role,
                value,
                terms,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region ReadAsync Tests

    /// <summary>
    /// Checks that reading a role that has a leader returns the leader's term and value.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenRoleHasALeader_ReturnsItsTermAndValue()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);

        Assert.Equal(role, view.Role);
        var leader = Assert.IsType<Leader>(view.Leader);
        var leaderValue = leader.Value.ToArray();
        Assert.Equal(leadership.Leader.Term, leader.Term);
        Assert.Equal(value, leaderValue);
    }

    /// <summary>
    /// Checks that reading a role that nobody leads returns a view with no leader.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenRoleHasNoLeader_ReturnsNoLeader()
    {
        var role = CreateKey("role");

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);

        Assert.Equal(role, view.Role);
        Assert.Null(view.Leader);
    }

    #endregion

    #region RenewAsync Tests

    /// <summary>
    /// Checks that renewing a leadership extends it from the renewal.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenLeadershipHolds_ExtendsItFromTheRenewal()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(BeforeExpiry, TestCancellationToken);
        var renewed = await Elections.RenewAsync(
            leadership,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        Assert.True(renewed);

        var before = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.NotNull(before.Leader);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(after.Leader);
    }

    /// <summary>
    /// Checks that renewing a leadership that has ended returns false.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenLeadershipEnded_ReturnsFalse()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);
        await Elections.ResignAsync(leadership, null, TestCancellationToken);

        var renewed = await Elections.RenewAsync(
            leadership,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.False(renewed);
    }

    /// <summary>
    /// Checks that renewing a leadership bound to a session throws an argument error, since it lives as its session
    /// does.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenLeadershipIsBoundToASession_ThrowsArgumentException()
    {
        var role = CreateKey("role");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var value = "node-a"u8.ToArray();
        var terms = LeaseTerms.ForSession(session);
        var result = await Elections.TryLeadAsync(role, value, terms, null, TestCancellationToken);
        Assert.True(result.IsElected);
        var leadership = result.Leadership;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Elections.RenewAsync(leadership, LongTimeToLive, null, TestCancellationToken)
        );

        Assert.Equal("leadership", exception.ParamName);
    }

    #endregion

    #region ResignAsync Tests

    /// <summary>
    /// Checks that resigning a leadership that holds returns true and vacates the role.
    /// </summary>
    [Fact]
    public async Task ResignAsync_WhenLeading_ReturnsTrueAndVacatesTheRole()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        var resigned = await Elections.ResignAsync(leadership, null, TestCancellationToken);

        Assert.True(resigned);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        Assert.Null(view.Leader);
    }

    /// <summary>
    /// Checks that resigning a leadership that has ended returns false and keeps the role's later leader.
    /// </summary>
    [Fact]
    public async Task ResignAsync_WhenNoLongerLeading_ReturnsFalse()
    {
        var role = CreateKey("role");
        var nodeA = "node-a"u8.ToArray();
        var nodeB = "node-b"u8.ToArray();
        var first = await LeadAsync(role, nodeA, LongTimeToLive, TestCancellationToken);
        await Elections.ResignAsync(first, null, TestCancellationToken);
        var second = await LeadAsync(role, nodeB, LongTimeToLive, TestCancellationToken);

        var resigned = await Elections.ResignAsync(first, null, TestCancellationToken);

        Assert.False(resigned);

        var view = await Elections.ReadAsync(role, null, TestCancellationToken);
        var leader = Assert.IsType<Leader>(view.Leader);
        Assert.Equal(second.Leader.Term, leader.Term);
    }

    #endregion

    #region WatchAsync Tests

    /// <summary>
    /// Checks that a watch yields the current view of the role first.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenStarted_YieldsTheCurrentViewFirst()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        var watch = Elections.WatchAsync(role, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);

        Assert.Equal(role, first.Role);
        var leader = Assert.IsType<Leader>(first.Leader);
        var leaderValue = leader.Value.ToArray();
        Assert.Equal(leadership.Leader.Term, leader.Term);
        Assert.Equal(value, leaderValue);
    }

    /// <summary>
    /// Checks that a watch yields a view with the term of a leader elected after the watch started.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenALeaderIsElected_YieldsItsTerm()
    {
        var role = CreateKey("role");
        var watch = Elections.WatchAsync(role, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);

        var view = await ContractStreams.NextMatchingAsync(
            stream,
            candidate => candidate.Leader is not null,
            TestCancellationToken
        );

        Assert.Null(first.Leader);
        var leader = Assert.IsType<Leader>(view.Leader);
        Assert.Equal(leadership.Leader.Term, leader.Term);
    }

    /// <summary>
    /// Checks that a watch yields a view with no leader when the leader resigns.
    /// </summary>
    [Fact]
    public async Task WatchAsync_WhenTheLeaderResigns_YieldsAVacancy()
    {
        var role = CreateKey("role");
        var value = "node-a"u8.ToArray();
        var leadership = await LeadAsync(role, value, LongTimeToLive, TestCancellationToken);
        var watch = Elections.WatchAsync(role, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);
        await Elections.ResignAsync(leadership, null, TestCancellationToken);

        var view = await ContractStreams.NextMatchingAsync(
            stream,
            candidate => candidate.Leader is null,
            TestCancellationToken
        );

        Assert.NotNull(first.Leader);
        Assert.Equal(role, view.Role);
    }

    #endregion
}
