// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the semaphore primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class SemaphoreContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's semaphore service, and skips the test when the provider does not support semaphores.
    /// </summary>
    private ISemaphoreService Semaphores => GetService<ISemaphoreService>(Primitives.Semaphore);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    /// <summary>
    /// Acquires a slot of the semaphore on a resource for a time to live and returns the grant, failing the test when
    /// no slot is acquired.
    /// </summary>
    /// <param name="resource">The resource to hold a slot of.</param>
    /// <param name="capacity">The number of slots.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The grant.</returns>
    private async ValueTask<SemaphoreGrant> AcquireAsync(
        CoordinationKey resource,
        int capacity,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var result = await TryAcquireAsync(resource, capacity, timeToLive, cancellationToken);

        Assert.True(result.IsAcquired);
        return result.Grant;
    }

    /// <summary>
    /// Tries to acquire a slot of the semaphore on a resource for a time to live and returns the result.
    /// </summary>
    /// <param name="resource">The resource to hold a slot of.</param>
    /// <param name="capacity">The number of slots.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The result of the attempt.</returns>
    private ValueTask<SemaphoreAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        int capacity,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        return Semaphores.TryAcquireAsync(resource, capacity, terms, null, cancellationToken);
    }

    /// <summary>
    /// Tries to acquire a slot of the semaphore on a resource for a time to live and records the call in a history.
    /// </summary>
    /// <param name="recorder">The recorder of the history.</param>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="resource">The resource to hold a slot of.</param>
    /// <param name="capacity">The number of slots.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The grant, or null when the call did not acquire a slot or did not return.</returns>
    private async ValueTask<SemaphoreGrant?> AcquireAsync(
        HistoryRecorder recorder,
        int client,
        CoordinationKey resource,
        int capacity,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var semaphores = Semaphores;
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        var call = new SemaphoreCall(SemaphoreCall.Acquire, 0, null, 0);

        var partition = resource.ToString();
        var operation = await recorder.RecordAsync(
            client,
            partition,
            call,
            token => semaphores.TryAcquireAsync(resource, capacity, terms, null, token),
            cancellationToken
        );

        return (operation.Output as SemaphoreAcquireResult)?.Grant;
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
        SemaphoreGrant grant,
        CancellationToken cancellationToken
    )
    {
        var semaphores = Semaphores;
        var call = new SemaphoreCall(
            SemaphoreCall.Renew,
            grant.Slot,
            grant.Owner.Value,
            grant.FencingToken.Value
        );

        var partition = grant.Resource.ToString();
        var operation = await recorder.RecordAsync(
            client,
            partition,
            call,
            token => semaphores.RenewAsync(grant, LongTimeToLive, null, token),
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
        SemaphoreGrant grant,
        CancellationToken cancellationToken
    )
    {
        var semaphores = Semaphores;
        var call = new SemaphoreCall(
            SemaphoreCall.Release,
            grant.Slot,
            grant.Owner.Value,
            grant.FencingToken.Value
        );

        var partition = grant.Resource.ToString();
        var operation = await recorder.RecordAsync(
            client,
            partition,
            call,
            token => semaphores.ReleaseAsync(grant, null, token),
            cancellationToken
        );

        return operation.Output is true;
    }

    #region TryAcquireAsync Tests

    /// <summary>
    /// Checks that holders of a semaphore with two slots each get a slot of their own.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenSlotsAreFree_GivesEachHolderItsOwnSlot()
    {
        var resource = CreateKey("resource");

        var first = await AcquireAsync(resource, 2, LongTimeToLive, TestCancellationToken);
        var second = await AcquireAsync(resource, 2, LongTimeToLive, TestCancellationToken);

        Assert.Equal(resource, first.Resource);
        Assert.NotEqual(first.Slot, second.Slot);
        Assert.InRange(first.Slot, 0, 1);
        Assert.InRange(second.Slot, 0, 1);
        Assert.NotEqual(first.Owner, second.Owner);
    }

    /// <summary>
    /// Checks that acquiring a slot when every slot is held returns a full result with no grant.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenEverySlotIsHeld_ReturnsFull()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);

        var result = await TryAcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);

        Assert.Equal(SemaphoreAcquireStatus.Full, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Checks that acquiring a slot with a capacity other than the holders' returns a capacity conflict.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenHoldersUseAnotherCapacity_ReturnsCapacityConflict()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, 2, LongTimeToLive, TestCancellationToken);

        var result = await TryAcquireAsync(resource, 3, LongTimeToLive, TestCancellationToken);

        Assert.Equal(SemaphoreAcquireStatus.CapacityConflict, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Checks that acquiring a slot with a capacity of zero throws an out of range error.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenCapacityIsZero_ThrowsArgumentOutOfRangeException()
    {
        var resource = CreateKey("resource");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await TryAcquireAsync(resource, 0, LongTimeToLive, TestCancellationToken)
        );

        Assert.Equal("capacity", exception.ParamName);
    }

    /// <summary>
    /// Checks that acquiring a slot with a capacity above the limit throws an out of range error.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenCapacityIsAboveTheLimit_ThrowsArgumentOutOfRangeException()
    {
        var resource = CreateKey("resource");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await TryAcquireAsync(
                resource,
                CoordinationLimits.MaxSemaphoreCapacity + 1,
                LongTimeToLive,
                TestCancellationToken
            )
        );

        Assert.Equal("capacity", exception.ParamName);
    }

    /// <summary>
    /// Checks that acquiring a slot with a capacity of exactly the limit succeeds.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenCapacityIsTheLimit_Acquires()
    {
        var resource = CreateKey("resource");

        var result = await TryAcquireAsync(
            resource,
            CoordinationLimits.MaxSemaphoreCapacity,
            LongTimeToLive,
            TestCancellationToken
        );

        Assert.True(result.IsAcquired);
    }

    /// <summary>
    /// Checks that acquiring a slot bound to a session that has ended returns a session ended result.
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

        var result = await Semaphores.TryAcquireAsync(
            resource,
            1,
            terms,
            null,
            TestCancellationToken
        );

        Assert.Equal(SemaphoreAcquireStatus.SessionEnded, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Checks that acquiring a slot again with the session of a grant already held returns that grant, and that
    /// another session still gets the other slot.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenTheSameSessionAcquiresAgain_ReturnsItsSlot()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenAsync(
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
        var terms = LeaseTerms.ForSession(session);
        var otherTerms = LeaseTerms.ForSession(other);
        var first = await Semaphores.TryAcquireAsync(
            resource,
            2,
            terms,
            null,
            TestCancellationToken
        );

        var second = await Semaphores.TryAcquireAsync(
            resource,
            2,
            terms,
            null,
            TestCancellationToken
        );
        var third = await Semaphores.TryAcquireAsync(
            resource,
            2,
            otherTerms,
            null,
            TestCancellationToken
        );

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.True(third.IsAcquired);
        Assert.Equal(first.Grant.Slot, second.Grant.Slot);
        Assert.Equal(first.Grant.Owner, second.Grant.Owner);
        Assert.Equal(first.Grant.FencingToken, second.Grant.FencingToken);
        Assert.NotEqual(first.Grant.Slot, third.Grant.Slot);
    }

    /// <summary>
    /// Checks that a slot acquired after a release gets a greater fencing token than the released grant's.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_AfterASlotIsReleased_GivesAGreaterFencingToken()
    {
        var resource = CreateKey("resource");
        var earlier = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        await Semaphores.ReleaseAsync(earlier, null, TestCancellationToken);

        var later = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);

        Assert.Equal(earlier.Slot, later.Slot);
        Assert.True(later.FencingToken > earlier.FencingToken);
    }

    /// <summary>
    /// Checks that once every holder has released, the semaphore accepts a different capacity.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenEveryHolderReleased_AcceptsAnotherCapacity()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        var result = await TryAcquireAsync(resource, 2, LongTimeToLive, TestCancellationToken);

        Assert.Equal(SemaphoreAcquireStatus.Acquired, result.Status);
    }

    /// <summary>
    /// Checks that a slot is freed when its grant's time to live passes, and that it stays held until then.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenAGrantExpires_FreesItsSlot()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var before = await TryAcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Full, before.Status);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await TryAcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Acquired, after.Status);
    }

    /// <summary>
    /// Checks that a slot is freed when the session it is bound to closes.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenTheSessionCloses_FreesItsSlot()
    {
        var resource = CreateKey("resource");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var terms = LeaseTerms.ForSession(session);
        var held = await Semaphores.TryAcquireAsync(
            resource,
            1,
            terms,
            null,
            TestCancellationToken
        );

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var result = await TryAcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        Assert.True(held.IsAcquired);
        Assert.Equal(SemaphoreAcquireStatus.Acquired, result.Status);
    }

    /// <summary>
    /// Checks that acquiring a slot with an option extension that the provider does not know throws an unsupported
    /// error.
    /// </summary>
    [Fact]
    public async Task TryAcquireAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var resource = CreateKey("resource");
        var terms = LeaseTerms.ForTimeToLive(LongTimeToLive);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Semaphores.TryAcquireAsync(
                resource,
                1,
                terms,
                UnknownExtension,
                TestCancellationToken
            )
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
        var grant = await AcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);

        await AdvanceClockAsync(BeforeExpiry, TestCancellationToken);
        var renewed = await Semaphores.RenewAsync(
            grant,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        Assert.True(renewed);

        var before = await TryAcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Full, before.Status);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var after = await TryAcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Acquired, after.Status);
    }

    /// <summary>
    /// Checks that renewing a grant that has ended returns false.
    /// </summary>
    [Fact]
    public async Task RenewAsync_WhenGrantEnded_ReturnsFalse()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        var renewed = await Semaphores.RenewAsync(
            grant,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

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
        var result = await Semaphores.TryAcquireAsync(
            resource,
            1,
            terms,
            null,
            TestCancellationToken
        );
        Assert.True(result.IsAcquired);
        var grant = result.Grant;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Semaphores.RenewAsync(grant, LongTimeToLive, null, TestCancellationToken)
        );

        Assert.Equal("grant", exception.ParamName);
    }

    #endregion

    #region ReleaseAsync Tests

    /// <summary>
    /// Checks that releasing a grant that holds returns true and frees its slot.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenGrantHolds_ReturnsTrueAndFreesTheSlot()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);

        var released = await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        Assert.True(released);

        var result = await TryAcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Acquired, result.Status);
    }

    /// <summary>
    /// Checks that releasing a grant that has ended returns false.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenGrantEnded_ReturnsFalse()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        var released = await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        Assert.False(released);
    }

    /// <summary>
    /// Checks that releasing a stale grant whose slot another holder took again returns false and keeps the new
    /// holder.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenTheSlotWasGrantedAgain_ReturnsFalseAndKeepsIt()
    {
        var resource = CreateKey("resource");
        var first = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        await Semaphores.ReleaseAsync(first, null, TestCancellationToken);
        await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);

        var released = await Semaphores.ReleaseAsync(first, null, TestCancellationToken);

        Assert.False(released);

        var result = await TryAcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        Assert.Equal(SemaphoreAcquireStatus.Full, result.Status);
    }

    #endregion

    #region WaitForReleaseAsync Tests

    /// <summary>
    /// Checks that waiting for a semaphore that has a free slot completes at once.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenASlotIsFree_CompletesAtOnce()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, 2, LongTimeToLive, TestCancellationToken);

        var wait = Semaphores.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a semaphore whose slots are all held completes when a holder releases its grant.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenAHolderReleases_Completes()
    {
        var resource = CreateKey("resource");
        var grant = await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        var wait = Semaphores.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

        Assert.False(wait.IsCompleted);

        await Semaphores.ReleaseAsync(grant, null, TestCancellationToken);

        await ContractStreams.CompletesAsync(wait, TestCancellationToken);
    }

    /// <summary>
    /// Checks that waiting for a semaphore whose slots are all held completes when the holder's time to live passes.
    /// </summary>
    [Fact]
    public async Task WaitForReleaseAsync_WhenTheSlotExpires_Completes()
    {
        var resource = CreateKey("resource");
        await AcquireAsync(resource, 1, ShortTimeToLive, TestCancellationToken);
        var wait = Semaphores.WaitForReleaseAsync(resource, null, TestCancellationToken).AsTask();

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
        await AcquireAsync(resource, 1, LongTimeToLive, TestCancellationToken);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestCancellationToken);
        var wait = Semaphores.WaitForReleaseAsync(resource, null, source.Token).AsTask();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that acquire, renew and release slots of a semaphore at once is
    /// linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Semaphore);
        var keys = CreateRunKeys();
        var held = new SemaphoreGrant?[ConcurrentRun.Clients];
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
                        SemaphoreModel.Capacity,
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

        var history = recorder.ToHistory();
        HistoryAssert.Linearizable(new SemaphoreModel(), history);
    }

    #endregion
}
