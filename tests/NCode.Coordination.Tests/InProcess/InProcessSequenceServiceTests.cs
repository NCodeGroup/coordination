// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessSequenceServiceTests
{
    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_WhenTheSequenceHandedOutTheLargestNumber_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessSequenceService();
        var key = InProcessTesting.Keyspace.CreateKey("full");
        service.SetLast(key, long.MaxValue);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.NextAsync(key, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenTheBlockWouldPassTheLargestNumber_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessSequenceService();
        var key = InProcessTesting.Keyspace.CreateKey("full");
        service.SetLast(key, long.MaxValue - 2);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ReserveAsync(key, 3, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheBlockWouldPassTheLargestNumber_KeepsTheSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessSequenceService();
        var key = InProcessTesting.Keyspace.CreateKey("full");
        service.SetLast(key, long.MaxValue - 2);

        await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ReserveAsync(key, 3, null, cancellationToken)
        );

        var next = await service.NextAsync(key, null, cancellationToken);

        Assert.Equal(long.MaxValue - 1, next);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheBlockEndsAtTheLargestNumber_ReservesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessSequenceService();
        var key = InProcessTesting.Keyspace.CreateKey("full");
        service.SetLast(key, long.MaxValue - 3);

        var block = await service.ReserveAsync(key, 3, null, cancellationToken);

        Assert.Equal(long.MaxValue - 2, block.First);
        Assert.Equal(3, block.Count);
    }

    #endregion
}
