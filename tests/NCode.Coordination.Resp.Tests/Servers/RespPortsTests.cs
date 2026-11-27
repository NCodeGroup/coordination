// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Tests.Servers;

public sealed class RespPortsTests
{
    #region StartOnFreePortsAsync Tests

    [Fact]
    public async Task StartOnFreePortsAsync_WhenTheFirstStartFails_StartsAgainWithTheRequestedNumberOfPorts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = 0;
        List<int> portCounts = [];
        Func<IReadOnlyList<int>, CancellationToken, ValueTask<string>> start = (ports, _) =>
        {
            attempts++;
            portCounts.Add(ports.Count);
            return attempts == 1
                ? ValueTask.FromException<string>(
                    new InvalidOperationException("The port is taken.")
                )
                : ValueTask.FromResult("started");
        };

        var result = await RespPorts.StartOnFreePortsAsync(2, start, cancellationToken);

        Assert.Equal("started", result);
        Assert.Equal(2, attempts);
        Assert.All(portCounts, count => Assert.Equal(2, count));
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
            return ValueTask.FromException<string>(
                new InvalidOperationException($"The port is taken, try {attempts}.")
            );
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
            return ValueTask.FromException<string>(new OperationCanceledException());
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
        var ports = RespPorts.Reserve(3);

        Assert.Equal(3, ports.Distinct().Count());
    }

    #endregion
}
