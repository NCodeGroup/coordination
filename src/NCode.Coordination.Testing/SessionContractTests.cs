// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the session primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class SessionContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    #region OpenAsync Tests

    /// <summary>
    /// Checks that opening a session grants its keyspace and at least the requested time to live.
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenCalled_GrantsAtLeastTheRequestedTimeToLive()
    {
        var grant = await Sessions.OpenAsync(Keyspace, LongTimeToLive, null, TestCancellationToken);

        Assert.Equal(Keyspace, grant.Id.Keyspace);
        Assert.True(grant.TimeToLive.Duration >= LongTimeToLive.Duration);
    }

    /// <summary>
    /// Checks that opening two sessions gives each its own id.
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenCalledTwice_GivesEachSessionItsOwnId()
    {
        var first = await Sessions.OpenAsync(Keyspace, LongTimeToLive, null, TestCancellationToken);

        var second = await Sessions.OpenAsync(
            Keyspace,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.NotEqual(first.Id, second.Id);
    }

    /// <summary>
    /// Checks that opening with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Sessions.OpenAsync(
                Keyspace,
                LongTimeToLive,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region KeepAliveAsync Tests

    /// <summary>
    /// Checks that renewing a live session returns a renewed result.
    /// </summary>
    [Fact]
    public async Task KeepAliveAsync_WhenSessionIsLive_ReturnsRenewed()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );

        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);

        Assert.True(result.IsRenewed);
        Assert.Equal(KeepAliveStatus.Renewed, result.Status);
    }

    /// <summary>
    /// Checks that renewing a session that was closed returns an ended result.
    /// </summary>
    [Fact]
    public async Task KeepAliveAsync_WhenSessionWasClosed_ReturnsEnded()
    {
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );

        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);

        Assert.False(result.IsRenewed);
        Assert.Equal(KeepAliveStatus.Ended, result.Status);
    }

    /// <summary>
    /// Checks that renewing a session that the provider does not know returns an ended result.
    /// </summary>
    [Fact]
    public async Task KeepAliveAsync_WhenSessionIsUnknown_ReturnsEnded()
    {
        var session = new SessionId(Keyspace, "conformance-unknown");

        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);

        Assert.Equal(KeepAliveStatus.Ended, result.Status);
    }

    /// <summary>
    /// Checks that renewing a session whose time to live has passed returns an ended result.
    /// </summary>
    [Fact]
    public async Task KeepAliveAsync_WhenTimeToLivePassed_ReturnsEnded()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            ShortTimeToLive,
            TestCancellationToken
        );

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);

        Assert.Equal(KeepAliveStatus.Ended, result.Status);
    }

    /// <summary>
    /// Checks that a renewal measures the time to live from the renewal, not from the opening.
    /// </summary>
    [Fact]
    public async Task KeepAliveAsync_WhenRenewedBeforeExpiry_MeasuresTheTimeToLiveFromTheRenewal()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            ShortTimeToLive,
            TestCancellationToken
        );
        var wait = Sessions.WaitForEndAsync(session, null, TestCancellationToken).AsTask();

        await AdvanceClockAsync(BeforeExpiry, TestCancellationToken);
        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);
        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        Assert.True(result.IsRenewed);
        Assert.False(wait.IsCompleted);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    #endregion

    #region CloseAsync Tests

    /// <summary>
    /// Checks that closing a session that has ended succeeds.
    /// </summary>
    [Fact]
    public async Task CloseAsync_WhenSessionHasEnded_Succeeds()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        await Sessions.CloseAsync(session, null, TestCancellationToken);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var result = await Sessions.KeepAliveAsync(session, null, TestCancellationToken);
        Assert.Equal(KeepAliveStatus.Ended, result.Status);
    }

    #endregion

    #region WaitForEndAsync Tests

    /// <summary>
    /// Checks that waiting for a session to end completes when the session is closed.
    /// </summary>
    [Fact]
    public async Task WaitForEndAsync_WhenTheSessionIsClosed_Completes()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var wait = Sessions.WaitForEndAsync(session, null, TestCancellationToken).AsTask();

        Assert.False(wait.IsCompleted);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a session to end completes when its time to live passes.
    /// </summary>
    [Fact]
    public async Task WaitForEndAsync_WhenTimeToLivePasses_Completes()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            ShortTimeToLive,
            TestCancellationToken
        );
        var wait = Sessions.WaitForEndAsync(session, null, TestCancellationToken).AsTask();

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        Assert.False(wait.IsCompleted);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a session that has ended completes at once.
    /// </summary>
    [Fact]
    public async Task WaitForEndAsync_WhenSessionHasEnded_CompletesAtOnce()
    {
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );

        var wait = Sessions.WaitForEndAsync(session, null, TestCancellationToken).AsTask();

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that cancelling the token of a wait throws an operation canceled exception.
    /// </summary>
    [Fact]
    public async Task WaitForEndAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestCancellationToken);
        var wait = Sessions.WaitForEndAsync(session, null, source.Token).AsTask();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    #endregion
}
