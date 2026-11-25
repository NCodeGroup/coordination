// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the set primitive: a provider's test project derives a sealed class from it and overrides
/// <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class SetContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's set service, and skips the test when the provider does not support sets.
    /// </summary>
    private ISetService Sets => GetService<ISetService>(Primitives.Set);

    /// <summary>
    /// Fills a set with the largest number of members, the numbers 0, 1 and so on.
    /// </summary>
    /// <param name="key">The key of the set.</param>
    /// <param name="timeToLive">The time to live of each member.</param>
    /// <param name="cancellationToken">A token that cancels the writes.</param>
    /// <returns>A task that completes when the set is full.</returns>
    private async ValueTask FillAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        for (var index = 0; index < CoordinationLimits.MaxSetMembers; index++)
        {
            var member = KeySegment.FromInt64(index);
            await Sets.AddAsync(key, member, timeToLive, null, cancellationToken);
        }
    }

    #region AddAsync Tests

    /// <summary>
    /// Checks that adding a member that is absent returns true and lists the member.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenMemberIsAbsent_ReturnsTrueAndListsIt()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");

        var added = await Sets.AddAsync(key, member, LongTimeToLive, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.True(added);
        Assert.Single(members);
        Assert.Contains(member, members);
    }

    /// <summary>
    /// Checks that adding a member that is already present returns false.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenMemberIsPresent_ReturnsFalse()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");
        await Sets.AddAsync(key, member, LongTimeToLive, null, TestCancellationToken);

        var added = await Sets.AddAsync(key, member, LongTimeToLive, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.False(added);
        Assert.Single(members);
    }

    /// <summary>
    /// Checks that adding a member that is already present replaces its expiry.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenMemberIsPresent_ReplacesItsExpiry()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");
        await Sets.AddAsync(key, member, ShortTimeToLive, null, TestCancellationToken);

        var beforeExpiry = TimeSpan.FromSeconds(1.5);
        await AdvanceClockAsync(beforeExpiry, TestCancellationToken);
        var readded = await Sets.AddAsync(
            key,
            member,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.False(readded);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        var alive = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.Contains(member, alive);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);
        var expired = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.Empty(expired);
    }

    /// <summary>
    /// Checks that each member expires on its own time to live.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenMembersHaveTheirOwnTimeToLive_ExpiresEachOnItsOwn()
    {
        var key = CreateKey("set");
        var shortLived = KeySegment.FromString("m1");
        var longLived = KeySegment.FromString("m2");
        await Sets.AddAsync(key, shortLived, ShortTimeToLive, null, TestCancellationToken);
        await Sets.AddAsync(key, longLived, LongTimeToLive, null, TestCancellationToken);

        var pastShortExpiry = TimeSpan.FromSeconds(3);
        await AdvanceClockAsync(pastShortExpiry, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.Single(members);
        Assert.Contains(longLived, members);
    }

    /// <summary>
    /// Checks that members of different kinds are kept apart even when they print alike.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenMembersDifferInKind_KeepsEach()
    {
        var key = CreateKey("set");
        var text = KeySegment.FromString("1");
        var number = KeySegment.FromInt64(1);
        var id = Guid.NewGuid();
        var uuid = KeySegment.FromGuid(id);

        var addedText = await Sets.AddAsync(key, text, LongTimeToLive, null, TestCancellationToken);
        var addedNumber = await Sets.AddAsync(
            key,
            number,
            LongTimeToLive,
            null,
            TestCancellationToken
        );
        var addedUuid = await Sets.AddAsync(key, uuid, LongTimeToLive, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.True(addedText);
        Assert.True(addedNumber);
        Assert.True(addedUuid);
        Assert.Equal(3, members.Length);
        Assert.Contains(text, members);
        Assert.Contains(number, members);
        Assert.Contains(uuid, members);
    }

    /// <summary>
    /// Checks that adding a member beyond the largest size of a set throws a limit exceeded error.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenTheSetIsFull_ThrowsLimitExceeded()
    {
        var key = CreateKey("set");
        await FillAsync(key, LongTimeToLive, TestCancellationToken);
        var extra = KeySegment.FromInt64(CoordinationLimits.MaxSetMembers);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Sets.AddAsync(key, extra, LongTimeToLive, null, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
    }

    /// <summary>
    /// Checks that adding a member that is already in a full set returns false.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenTheSetIsFullAndMemberIsPresent_ReturnsFalse()
    {
        var key = CreateKey("set");
        await FillAsync(key, LongTimeToLive, TestCancellationToken);
        var first = KeySegment.FromInt64(0);

        var added = await Sets.AddAsync(key, first, LongTimeToLive, null, TestCancellationToken);

        Assert.False(added);
    }

    /// <summary>
    /// Checks that a set whose every member expired accepts a new member, because only live members count toward
    /// the limit.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenEveryMemberOfAFullSetExpired_AddsTheMember()
    {
        var key = CreateKey("set");
        await FillAsync(key, ShortTimeToLive, TestCancellationToken);
        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);
        var extra = KeySegment.FromInt64(CoordinationLimits.MaxSetMembers);

        var added = await Sets.AddAsync(key, extra, LongTimeToLive, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.True(added);
        Assert.Single(members);
        Assert.Contains(extra, members);
    }

    /// <summary>
    /// Checks that adding with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Sets.AddAsync(
                key,
                member,
                LongTimeToLive,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region RemoveAsync Tests

    /// <summary>
    /// Checks that removing a member that is present returns true and removes it.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_WhenMemberIsPresent_ReturnsTrueAndRemovesIt()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");
        await Sets.AddAsync(key, member, LongTimeToLive, null, TestCancellationToken);

        var removed = await Sets.RemoveAsync(key, member, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.True(removed);
        Assert.Empty(members);
    }

    /// <summary>
    /// Checks that removing a member that is absent returns false.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_WhenMemberIsAbsent_ReturnsFalse()
    {
        var key = CreateKey("set");
        var member = KeySegment.FromString("m1");

        var removed = await Sets.RemoveAsync(key, member, null, TestCancellationToken);

        Assert.False(removed);
    }

    #endregion

    #region ListAsync Tests

    /// <summary>
    /// Checks that listing a set that was never written returns no members.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenSetIsAbsent_ReturnsEmpty()
    {
        var key = CreateKey("set");

        var members = await Sets.ListAsync(key, null, TestCancellationToken);

        Assert.Empty(members);
    }

    /// <summary>
    /// Checks that listing with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var key = CreateKey("set");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Sets.ListAsync(key, null, canceledToken)
        );
    }

    #endregion

    #region ClearAsync Tests

    /// <summary>
    /// Checks that clearing a set removes each of its members.
    /// </summary>
    [Fact]
    public async Task ClearAsync_WhenSetHasMembers_RemovesEach()
    {
        var key = CreateKey("set");
        var first = KeySegment.FromString("m1");
        var second = KeySegment.FromString("m2");
        await Sets.AddAsync(key, first, LongTimeToLive, null, TestCancellationToken);
        await Sets.AddAsync(key, second, LongTimeToLive, null, TestCancellationToken);

        await Sets.ClearAsync(key, null, TestCancellationToken);

        var members = await Sets.ListAsync(key, null, TestCancellationToken);
        Assert.Empty(members);
    }

    #endregion
}
