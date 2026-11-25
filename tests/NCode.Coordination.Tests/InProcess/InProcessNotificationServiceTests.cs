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
        using var provider = new InProcessProvider(InProcessTesting.CreateTime());
        var service = GetService(provider);
        var topic = InProcessTesting.Keyspace.CreateKey("t");
        await using var stream = service
            .SubscribeAsync(topic, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        await service.PublishAsync(topic, "a"u8.ToArray(), null, cancellationToken);
        moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.Equal(1, stream.Current.Sequence);
        for (var index = 0; index <= InProcessNotificationService.QueueCapacity; index++)
        {
            await service.PublishAsync(topic, "a"u8.ToArray(), null, cancellationToken);
        }

        moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        Assert.Equal(topic, stream.Current.Topic);
        Assert.Equal(1, stream.Current.Sequence);

        await service.PublishAsync(topic, "b"u8.ToArray(), null, cancellationToken);

        moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.False(stream.Current.IsResync);
        Assert.Equal(InProcessNotificationService.QueueCapacity + 3, stream.Current.Sequence);
        Assert.Equal("b"u8.ToArray(), stream.Current.Payload.ToArray());
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenTopicsDiffer_NumbersEachOnItsOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = new InProcessProvider(InProcessTesting.CreateTime());
        var service = GetService(provider);
        var first = InProcessTesting.Keyspace.CreateKey("first");
        var second = InProcessTesting.Keyspace.CreateKey("second");
        await using var firstStream = service
            .SubscribeAsync(first, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        await using var secondStream = service
            .SubscribeAsync(second, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        Assert.True(await firstStream.MoveNextAsync());
        Assert.True(await secondStream.MoveNextAsync());

        await service.PublishAsync(first, "a"u8.ToArray(), null, cancellationToken);
        await service.PublishAsync(first, "a"u8.ToArray(), null, cancellationToken);
        await service.PublishAsync(second, "a"u8.ToArray(), null, cancellationToken);

        Assert.True(await firstStream.MoveNextAsync());
        Assert.Equal(1, firstStream.Current.Sequence);
        Assert.True(await firstStream.MoveNextAsync());
        Assert.Equal(2, firstStream.Current.Sequence);
        Assert.True(await secondStream.MoveNextAsync());
        Assert.Equal(1, secondStream.Current.Sequence);
        Assert.Equal(second, secondStream.Current.Topic);
    }

    #endregion
}
