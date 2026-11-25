// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the lease primitive: a provider's test project derives a sealed class from it and overrides
/// <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class LeaseContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's lease service, and skips the test when the provider does not support leases.
    /// </summary>
    private ILeaseService Leases => GetService<ILeaseService>(Primitives.Lease);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    /// <summary>
    /// Acquires the lease on a resource for a time to live and returns the grant, failing the test when the lease is
    /// not acquired.
    /// </summary>
    /// <param name="resource">The resource to lease.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The grant.</returns>
    private async ValueTask<LeaseGrant> AcquireAsync(
        CoordinationKey resource,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        var result = await Leases.TryAcquireAsync(resource, terms, null, cancellationToken);

        Assert.True(result.IsAcquired);
        return result.Grant;
    }

    /// <summary>
    /// Tries to acquire the lease on a resource for a time to live and returns the result.
    /// </summary>
    /// <param name="resource">The resource to lease.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The result of the attempt.</returns>
    private ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        return Leases.TryAcquireAsync(resource, terms, null, cancellationToken);
    }

    /// <summary>
    /// Tries to acquire the lease on a resource for a time to live and records the call in a history.
    /// </summary>
    /// <param name="recorder">The recorder of the history.</param>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="resource">The resource to lease.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The grant, or null when the call did not acquire the lease or did not return.</returns>
    private async ValueTask<LeaseGrant?> AcquireAsync(
        HistoryRecorder recorder,
        int client,
        CoordinationKey resource,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var leases = Leases;
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        var call = new LeaseCall(LeaseCall.Acquire, null, 0, timeToLive.Duration);

        var operation = await recorder.RecordAsync(
            client,
            resource.ToString(),
            call,
            token => leases.TryAcquireAsync(resource, terms, null, token),
            cancellationToken
        );

        return (operation.Output as LeaseAcquireResult)?.Grant;
    }

    /// <summary>
    /// Renews a grant for the long time to live and records the call in a history.
    /// </summary>
    /// <param name="recorder">The recorder of the history.</param>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="grant">The grant to renew.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>True when the call returned and renewed the grant; otherwise, false.</returns>
    private async ValueTask<bool> RenewAsync(
        HistoryRecorder recorder,
        int client,
        LeaseGrant grant,
        CancellationToken cancellationToken
    )
    {
        var leases = Leases;
        var call = new LeaseCall(
            LeaseCall.Renew,
            grant.Owner.Value,
            grant.FencingToken.Value,
            LongTimeToLive.Duration
        );

        var operation = await recorder.RecordAsync(
            client,
            grant.Resource.ToString(),
            call,
            token => leases.RenewAsync(grant, LongTimeToLive, null, token),
            cancellationToken
        );

        return operation.Output is true;
    }

    /// <summary>
    /// Releases a grant and records the call in a history.
    /// </summary>
    /// <param name="recorder">The recorder of the history.</param>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="grant">The grant to release.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>True when the call returned and released the grant; otherwise, false.</returns>
    private async ValueTask<bool> ReleaseAsync(
        HistoryRecorder recorder,
        int client,
        LeaseGrant grant,
        CancellationToken cancellationToken
    )
    {
        var leases = Leases;
        var call = new LeaseCall(
            LeaseCall.Release,
            grant.Owner.Value,
            grant.FencingToken.Value,
            TimeSpan.Zero
        );

        var operation = await recorder.RecordAsync(
            client,
            grant.Resource.ToString(),
            call,
            token => leases.ReleaseAsync(grant, null, token),
            cancellationToken
        );

        return operation.Output is true;
    }

    #region TryAcquireAsync Tests

    /// <summary>
    /// Checks that acquiring a lease that nobody holds returns a grant for the resource with the terms asked for.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenResourceIsFree_ReturnsAGrant()
    {
        var resource = CreateKey("resource");

        var result = await TryAcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        Assert.Equal(LeaseAcquireStatus.Acquired, result.Status);
        var grant = Assert.IsType<LeaseGrant>(result.Grant);
        Assert.Equal(resource, grant.Resource);
        Assert.False(grant.Terms.IsBoundToSession);
        Assert.True(grant.Terms.TimeToLive.Duration >= LongTimeToLive.Duration);
    }

    /// <summary>
    /// Checks that acquiring a lease that another holder has returns a held result with no grant.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenAnotherHolderHasIt_ReturnsHeld()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        var result = await TryAcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        Assert.Equal(LeaseAcquireStatus.Held, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Checks that acquiring a lease again with the session of a grant already held returns that grant.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenTheSameSessionAcquiresAgain_ReturnsTheExistingGrant()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var terms = LeaseTerms.ForSession(session);
        var first = await Leases.TryAcquireAsync(resource, terms, null, TestCancellationToken);

        var second = await Leases.TryAcquireAsync(resource, terms, null, TestCancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.Equal(first.Grant.Owner, second.Grant.Owner);
        Assert.Equal(first.Grant.FencingToken, second.Grant.FencingToken);
    }

    /// <summary>
    /// Checks that acquiring a lease that the grant of another session holds returns a held result.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenAnotherSessionHoldsIt_ReturnsHeld()
    {
        var resource = CreateKey("resource");
        var holder = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var other = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var holderTerms = LeaseTerms.ForSession(holder);
        var otherTerms = LeaseTerms.ForSession(other);
        var held = await Leases.TryAcquireAsync(resource, holderTerms, null, TestCancellationToken);

        var result = await Leases.TryAcquireAsync(
            resource,
            otherTerms,
            null,
            TestCancellationToken
        );

        Assert.True(held.IsAcquired);
        Assert.Equal(LeaseAcquireStatus.Held, result.Status);
    }

    /// <summary>
    /// Checks that acquiring a lease bound to a session that has ended returns a session ended result.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenSessionHasEnded_ReturnsSessionEnded()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );

        var terms = LeaseTerms.ForSession(session);

        var result = await Leases.TryAcquireAsync(resource, terms, null, TestCancellationToken);

        Assert.Equal(LeaseAcquireStatus.SessionEnded, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Checks that a lease acquired after a release gets a greater fencing token than the released grant's.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_AfterRelease_GivesAGreaterFencingToken()
    {
        var resource = CreateKey("resource");
        var earlier = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        await Leases.ReleaseAsync(earlier, null, TestCancellationToken);

        var later = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        Assert.True(later.FencingToken > earlier.FencingToken);
    }

    /// <summary>
    /// Checks that a lease acquired after a grant's time to live passed gets a greater fencing token, and that the
    /// resource stays held until the time to live passes.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_AfterExpiry_GivesAGreaterFencingToken()
    {
        var resource = CreateKey("resource");
        var earlier = await AcquireAsync(resource, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var before = await TryAcquireAsync(resource, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(LeaseAcquireStatus.Held, before.Status);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var later = await AcquireAsync(resource, ShortTimeToLive, TestCancellationToken);
        Assert.True(later.FencingToken > earlier.FencingToken);
    }

    /// <summary>
    /// Checks that a lease is freed when the session it is bound to closes.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenTheSessionCloses_FreesTheResource()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var terms = LeaseTerms.ForSession(session);
        var held = await Leases.TryAcquireAsync(resource, terms, null, TestCancellationToken);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var result = await TryAcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        Assert.True(held.IsAcquired);
        Assert.Equal(LeaseAcquireStatus.Acquired, result.Status);
    }

    /// <summary>
    /// Checks that acquiring a lease with an option extension that the provider does not know throws an unsupported
    /// error.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var resource = CreateKey("resource");
        var terms = LeaseTerms.ForTimeToLive(LongTimeToLive);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Leases.TryAcquireAsync(resource, terms, UnknownExtension, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region RenewAsync Tests

    /// <summary>
    /// Checks that renewing a grant extends it from the renewal.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenGrantHolds_ExtendsItFromTheRenewal()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(BeforeExpiry, TestCancellationToken);
        var renewed = await Leases.RenewAsync(grant, ShortTimeToLive, null, TestCancellationToken);
        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        Assert.True(renewed);

        var before = await TryAcquireAsync(resource, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(LeaseAcquireStatus.Held, before.Status);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await TryAcquireAsync(resource, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(LeaseAcquireStatus.Acquired, after.Status);
    }

    /// <summary>
    /// Checks that renewing a grant that has ended returns false.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenGrantEnded_ReturnsFalse()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        await Leases.ReleaseAsync(grant, null, TestCancellationToken);

        var renewed = await Leases.RenewAsync(grant, LongTimeToLive, null, TestCancellationToken);

        Assert.False(renewed);
    }

    /// <summary>
    /// Checks that renewing a grant bound to a session throws an argument error, since it lives as its session does.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenGrantIsBoundToASession_ThrowsArgumentException()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var terms = LeaseTerms.ForSession(session);
        var result = await Leases.TryAcquireAsync(resource, terms, null, TestCancellationToken);
        Assert.True(result.IsAcquired);
        var grant = result.Grant;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Leases.RenewAsync(grant, LongTimeToLive, null, TestCancellationToken)
        );

        Assert.Equal("grant", exception.ParamName);
    }

    #endregion

    #region ReleaseAsync Tests

    /// <summary>
    /// Checks that releasing a grant that holds returns true and frees the resource.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenGrantHolds_ReturnsTrueAndFreesTheResource()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        var released = await Leases.ReleaseAsync(grant, null, TestCancellationToken);

        Assert.True(released);

        var result = await TryAcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        Assert.Equal(LeaseAcquireStatus.Acquired, result.Status);
    }

    /// <summary>
    /// Checks that releasing a grant that has ended returns false and keeps the grant of the resource's later holder.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenAnotherGrantHoldsTheResource_ReturnsFalseAndKeepsIt()
    {
        var resource = CreateKey("resource");
        var first = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        await Leases.ReleaseAsync(first, null, TestCancellationToken);
        await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);

        var released = await Leases.ReleaseAsync(first, null, TestCancellationToken);

        Assert.False(released);

        var result = await TryAcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        Assert.Equal(LeaseAcquireStatus.Held, result.Status);
    }

    #endregion

    #region WaitForReleaseAsync Tests

    /// <summary>
    /// Checks that waiting for a resource that nobody holds completes at once.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenResourceIsFree_CompletesAtOnce()
    {
        var resource = CreateKey("resource");

        var wait = Leases.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a resource completes when its holder releases the grant.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenTheHolderReleases_Completes()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        var wait = Leases.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

        Assert.False(wait.IsCompleted);

        await Leases.ReleaseAsync(grant, null, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a resource completes when the grant's time to live passes.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenTheGrantExpires_Completes()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, ShortTimeToLive, TestCancellationToken);
        var wait = Leases.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        Assert.False(wait.IsCompleted);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that cancelling the token of a wait throws an operation canceled exception.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, LongTimeToLive, TestCancellationToken);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestCancellationToken);
        var wait = Leases.WaitForReleaseAsync(resource, null, source.Token).AsTask();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that acquire, renew and release leases at once is linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Lease);
        var keys = CreateRunKeys();
        var held = new LeaseGrant?[ConcurrentRun.Clients];
        var recorder = new HistoryRecorder();

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var grant = held[client];
                if (grant is null)
                {
                    var key = keys[(client + index) % keys.Length];
                    held[client] = await AcquireAsync(
                        recorder,
                        client,
                        key,
                        LongTimeToLive,
                        cancellationToken
                    );
                    return;
                }

                if (index % 3 == 0)
                {
                    await ReleaseAsync(recorder, client, grant, cancellationToken);
                    held[client] = null;
                    return;
                }

                if (!await RenewAsync(recorder, client, grant, cancellationToken))
                {
                    held[client] = null;
                }
            },
            TestCancellationToken
        );

        HistoryAssert.Linearizable(new LeaseModel(), recorder.ToHistory());
    }

    /// <summary>
    /// Checks that the history is linearizable when a holder pauses until its lease has expired and then renews and
    /// releases the lease it no longer holds, while other clients acquire it.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenAHolderPauses_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Lease);
        Assert.SkipWhen(
            Target.AdvanceClock is null,
            "The target cannot advance its provider's clock."
        );
        var key = CreateKey("run", "paused");
        var held = new LeaseGrant?[ConcurrentRun.Clients];
        var recorder = new HistoryRecorder();

        var stale = await AcquireAsync(recorder, 0, key, ShortTimeToLive, TestCancellationToken);
        Assert.NotNull(stale);

        var pause = StillAlive + PastExpiry;
        await recorder.RecordAsync(
            HistoryOperation.TestClient,
            HistoryOperation.EveryPartition,
            new ClockAdvance(pause),
            token => AdvanceClockAsync(pause, token),
            TestCancellationToken
        );

        await ConcurrentRun.RunAsync(
            4,
            async (client, index, cancellationToken) =>
            {
                if (client == 0)
                {
                    if (index % 2 == 0)
                    {
                        await RenewAsync(recorder, client, stale, cancellationToken);
                    }
                    else
                    {
                        await ReleaseAsync(recorder, client, stale, cancellationToken);
                    }

                    return;
                }

                var grant = held[client];
                if (grant is null)
                {
                    held[client] = await AcquireAsync(
                        recorder,
                        client,
                        key,
                        LongTimeToLive,
                        cancellationToken
                    );
                    return;
                }

                if (index == 2)
                {
                    await RenewAsync(recorder, client, grant, cancellationToken);
                    return;
                }

                if (index == 3)
                {
                    await ReleaseAsync(recorder, client, grant, cancellationToken);
                    held[client] = null;
                }
            },
            null,
            TestCancellationToken
        );

        HistoryAssert.Linearizable(new LeaseModel(), recorder.ToHistory());
    }

    #endregion
}
