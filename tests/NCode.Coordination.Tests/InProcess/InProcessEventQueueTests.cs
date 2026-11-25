// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessEventQueueTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenCapacityIsBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InProcessEventQueue<string>(0));
    }

    #endregion

    #region TakeAsync Tests

    [Fact]
    public async Task TakeAsync_WhenItemsWerePosted_ReturnsThemInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new InProcessEventQueue<string>(4);

        queue.Post("a");
        queue.Post("b");

        var first = await queue.TakeAsync(cancellationToken);
        var second = await queue.TakeAsync(cancellationToken);

        var expectedFirst = new InProcessQueued<string>("a", Overflowed: false);
        var expectedSecond = new InProcessQueued<string>("b", Overflowed: false);
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedSecond, second);
    }

    [Fact]
    public async Task TakeAsync_WhenEmpty_WaitsForAPost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new InProcessEventQueue<string>(4);

        var take = queue.TakeAsync(cancellationToken).AsTask();

        Assert.False(take.IsCompleted);

        queue.Post("a");

        var queued = await take;

        var expected = new InProcessQueued<string>("a", Overflowed: false);
        Assert.Equal(expected, queued);
    }

    [Fact]
    public async Task TakeAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var queue = new InProcessEventQueue<string>(4);

        var take = queue.TakeAsync(source.Token).AsTask();

        Assert.False(take.IsCompleted);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => take);
    }

    #endregion

    #region Post Tests

    [Fact]
    public async Task Post_WhenTheQueueIsFull_DropsWhatItHoldsAndReportsTheNewestItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new InProcessEventQueue<string>(2);

        queue.Post("a");
        queue.Post("b");
        queue.Post("c");

        var overflow = await queue.TakeAsync(cancellationToken);

        queue.Post("d");

        var next = await queue.TakeAsync(cancellationToken);

        var expectedOverflow = new InProcessQueued<string>("c", Overflowed: true);
        var expectedNext = new InProcessQueued<string>("d", Overflowed: false);
        Assert.Equal(expectedOverflow, overflow);
        Assert.Equal(expectedNext, next);
    }

    #endregion
}
