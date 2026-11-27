// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Tests.Servers;

public sealed class RespPortsTests
{
    #region StartOnFreePortsAsync Tests

    [Fact]
    public async Task StartOnFreePortsAsync_WhenTheFirstStartFails_StartsAgainWithTheRequestedNumberOfPorts()
    {
        const int requestedPorts = 2;
        const string started = "started";
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = 0;
        List<int> portCounts = [];
        Func<IReadOnlyList<int>, CancellationToken, ValueTask<string>> start = (ports, _) =>
        {
            attempts++;
            portCounts.Add(ports.Count);
            var failure = new InvalidOperationException("The port is taken.");
            return attempts == 1
                ? ValueTask.FromException<string>(failure)
                : ValueTask.FromResult(started);
        };

        var result = await RespPorts.StartOnFreePortsAsync(
            requestedPorts,
            start,
            cancellationToken
        );

        Assert.Equal(started, result);
        Assert.Equal(2, attempts);
        Assert.All(portCounts, count => Assert.Equal(requestedPorts, count));
    }

    [Fact]
    public async Task StartOnFreePortsAsync_WhenEveryStartFails_ThrowsAnAggregateOfEveryFailure()
    {
        const int tries = 3;
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = 0;
        Func<IReadOnlyList<int>, CancellationToken, ValueTask<string>> start = (_, _) =>
        {
            attempts++;
            var failure = new InvalidOperationException($"The port is taken, try {attempts}.");
            return ValueTask.FromException<string>(failure);
        };

        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await RespPorts.StartOnFreePortsAsync(1, start, cancellationToken)
        );

        Assert.Equal(tries, attempts);
        Assert.Equal(tries, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task StartOnFreePortsAsync_WhenTheStartIsCancelled_DoesNotStartAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = 0;
        Func<IReadOnlyList<int>, CancellationToken, ValueTask<string>> start = (_, _) =>
        {
            attempts++;
            var failure = new OperationCanceledException();
            return ValueTask.FromException<string>(failure);
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await RespPorts.StartOnFreePortsAsync(1, start, cancellationToken)
        );

        Assert.Equal(1, attempts);
    }

    #endregion

    #region Reserve Tests

    [Fact]
    public void Reserve_WhenThreePortsAreAsked_ReturnsThreeDistinctPorts()
    {
        const int count = 3;

        var ports = RespPorts.Reserve(count);
        var distinctCount = ports.Distinct().Count();

        Assert.Equal(count, distinctCount);
    }

    #endregion
}
