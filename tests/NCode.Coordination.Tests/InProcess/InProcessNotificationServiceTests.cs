// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessNotificationServiceTests
{
    private static INotificationService GetService(InProcessProvider provider)
    {
        var service = provider.GetPrimitiveService(typeof(INotificationService));
        return Assert.IsAssignableFrom<INotificationService>(service);
    }

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WhenTheSubscriberFallsBehind_YieldsAResyncWithTheLastDeliveredSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var service = GetService(provider);
        var topic = InProcessTesting.Keyspace.CreateKey("t");
        var payload = "a"u8.ToArray();
        await using var stream = service
            .SubscribeAsync(topic, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        await service.PublishAsync(topic, payload, null, cancellationToken);
        moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.Equal(1, stream.Current.Sequence);
        for (var index = 0; index <= InProcessNotificationService.QueueCapacity; index++)
        {
            await service.PublishAsync(topic, payload, null, cancellationToken);
        }

        moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        Assert.Equal(topic, stream.Current.Topic);
        Assert.Equal(1, stream.Current.Sequence);

        var next = "b"u8.ToArray();
        await service.PublishAsync(topic, next, null, cancellationToken);

        moved = await stream.MoveNextAsync();
        var actual = stream.Current.Payload.ToArray();

        Assert.True(moved);
        Assert.False(stream.Current.IsResync);
        Assert.Equal(InProcessNotificationService.QueueCapacity + 3, stream.Current.Sequence);
        Assert.Equal(next, actual);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenTopicsDiffer_NumbersEachOnItsOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var service = GetService(provider);
        var first = InProcessTesting.Keyspace.CreateKey("first");
        var second = InProcessTesting.Keyspace.CreateKey("second");
        var payload = "a"u8.ToArray();
        await using var firstStream = service
            .SubscribeAsync(first, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        await using var secondStream = service
            .SubscribeAsync(second, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var firstMoved = await firstStream.MoveNextAsync();
        var secondMoved = await secondStream.MoveNextAsync();
        Assert.True(firstMoved);
        Assert.True(secondMoved);

        await service.PublishAsync(first, payload, null, cancellationToken);
        await service.PublishAsync(first, payload, null, cancellationToken);
        await service.PublishAsync(second, payload, null, cancellationToken);

        firstMoved = await firstStream.MoveNextAsync();
        Assert.True(firstMoved);
        Assert.Equal(1, firstStream.Current.Sequence);
        firstMoved = await firstStream.MoveNextAsync();
        Assert.True(firstMoved);
        Assert.Equal(2, firstStream.Current.Sequence);
        secondMoved = await secondStream.MoveNextAsync();
        Assert.True(secondMoved);
        Assert.Equal(1, secondStream.Current.Sequence);
        Assert.Equal(second, secondStream.Current.Topic);
    }

    #endregion
}
