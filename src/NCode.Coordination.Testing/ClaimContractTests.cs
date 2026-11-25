// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the claim primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class ClaimContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the instant one hour after the Unix epoch, where the first window starts.
    /// </summary>
    private static DateTimeOffset FirstStart { get; } = DateTimeOffset.UnixEpoch.AddHours(1);

    /// <summary>
    /// Gets the instant two hours after the Unix epoch, where the second window starts.
    /// </summary>
    private static DateTimeOffset SecondStart { get; } = DateTimeOffset.UnixEpoch.AddHours(2);

    /// <summary>
    /// Gets the length of both windows, one hour.
    /// </summary>
    private static TimeSpan WindowLength { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the window of one hour that starts one hour after the Unix epoch.
    /// </summary>
    private static TimeWindow FirstWindow { get; } = new(FirstStart, WindowLength);

    /// <summary>
    /// Gets the window of one hour that starts two hours after the Unix epoch.
    /// </summary>
    private static TimeWindow SecondWindow { get; } = new(SecondStart, WindowLength);

    /// <summary>
    /// Gets the provider's claim service, and skips the test when the provider does not support claims.
    /// </summary>
    private IClaimService Claims => GetService<IClaimService>(Primitives.Claim);

    #region TryClaimAsync Tests

    /// <summary>
    /// Checks that claiming a name in a window that nobody claimed returns claimed.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenTheWindowIsUnclaimed_ReturnsClaimed()
    {
        var name = CreateKey("a");

        var status = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, status);
    }

    /// <summary>
    /// Checks that claiming a name in a window that was already claimed returns taken.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenTheWindowWasClaimed_ReturnsTaken()
    {
        var name = CreateKey("a");
        await Claims.TryClaimAsync(name, FirstWindow, LongTimeToLive, null, TestCancellationToken);

        var status = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Taken, status);
    }

    /// <summary>
    /// Checks that windows that differ are claimed independently of each other.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenWindowsDiffer_ClaimsEach()
    {
        var name = CreateKey("a");

        var first = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var second = await Claims.TryClaimAsync(
            name,
            SecondWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, first);
        Assert.Equal(ClaimStatus.Claimed, second);
    }

    /// <summary>
    /// Checks that names that differ are claimed independently of each other.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenNamesDiffer_ClaimsEach()
    {
        var firstName = CreateKey("a");
        var secondName = CreateKey("b");

        var first = await Claims.TryClaimAsync(
            firstName,
            FirstWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var second = await Claims.TryClaimAsync(
            secondName,
            FirstWindow,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, first);
        Assert.Equal(ClaimStatus.Claimed, second);
    }

    /// <summary>
    /// Checks that a claim whose time to live has not passed is still taken.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenTimeToLiveHasNotPassed_ReturnsTaken()
    {
        var name = CreateKey("a");
        var first = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(ClaimStatus.Claimed, first);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var status = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Taken, status);
    }

    /// <summary>
    /// Checks that a claim whose time to live passed is claimed again.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenTimeToLivePassed_ReturnsClaimedAgain()
    {
        var name = CreateKey("a");
        var first = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(ClaimStatus.Claimed, first);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var status = await Claims.TryClaimAsync(
            name,
            FirstWindow,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, status);
    }

    /// <summary>
    /// Checks that claiming with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var name = CreateKey("a");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Claims.TryClaimAsync(
                name,
                FirstWindow,
                LongTimeToLive,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    /// <summary>
    /// Checks that claiming with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task TryClaimAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var name = CreateKey("a");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Claims.TryClaimAsync(name, FirstWindow, LongTimeToLive, null, canceledToken)
        );
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that claim the same windows at once is linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Claim);
        var claims = Claims;
        var keys = CreateRunKeys();
        var recorder = new HistoryRecorder();

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var key = keys[(client + index) % keys.Length];
                var call = new ClaimCall(ClaimCall.Claim);
                var partition = key.ToString();
                await recorder.RecordAsync(
                    client,
                    partition,
                    call,
                    token => claims.TryClaimAsync(key, FirstWindow, LongTimeToLive, null, token),
                    cancellationToken
                );
            },
            TestCancellationToken
        );

        var history = recorder.ToHistory();
        HistoryAssert.Linearizable(new ClaimModel(), history);
    }

    #endregion
}
