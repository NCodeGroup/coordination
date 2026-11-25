// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class DroppingProviderTests
{
    #region GetPrimitiveService Tests

    [Fact]
    public void GetPrimitiveService_WhenSessions_ReturnsTheInnerService()
    {
        var inner = new InProcessProvider(InProcessTesting.CreateTime());
        using var provider = new DroppingProvider(inner);

        var service = provider.GetPrimitiveService(typeof(ISessionService));

        Assert.Same(inner.GetPrimitiveService(typeof(ISessionService)), service);
    }

    [Fact]
    public async Task GetPrimitiveService_WhenRecordsAfterADrop_FailsThreeCreatesAndWritesTheFourth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inner = new InProcessProvider(InProcessTesting.CreateTime());
        using var provider = new DroppingProvider(inner);
        var service = Assert.IsAssignableFrom<IRecordService>(
            provider.GetPrimitiveService(typeof(IRecordService))
        );
        var a = InProcessTesting.Keyspace.CreateKey("a");
        var b = InProcessTesting.Keyspace.CreateKey("b");
        var c = InProcessTesting.Keyspace.CreateKey("c");
        var d = InProcessTesting.Keyspace.CreateKey("d");
        ReadOnlyMemory<byte> value = (byte[])[1];
        await provider.Connection.DropAsync(cancellationToken);

        var first = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(a, value, RecordLifetime.Persistent, null, cancellationToken)
        );
        var second = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(b, value, RecordLifetime.Persistent, null, cancellationToken)
        );
        var third = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(c, value, RecordLifetime.Persistent, null, cancellationToken)
        );
        var fourth = await service.CreateAsync(
            d,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var readA = await service.ReadAsync(a, null, cancellationToken);
        var readB = await service.ReadAsync(b, null, cancellationToken);
        var readC = await service.ReadAsync(c, null, cancellationToken);
        var readD = await service.ReadAsync(d, null, cancellationToken);

        Assert.True(first.OutcomeUnknown);
        Assert.True(second.OutcomeUnknown);
        Assert.True(third.OutcomeUnknown);
        Assert.True(fourth.IsWritten);
        Assert.Null(readA);
        Assert.NotNull(readB);
        Assert.Null(readC);
        Assert.NotNull(readD);
    }

    [Fact]
    public async Task GetPrimitiveService_WhenNotificationsAfterADrop_YieldsAResyncInPlaceOfTheLostMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inner = new InProcessProvider(InProcessTesting.CreateTime());
        using var provider = new DroppingProvider(inner);
        var service = Assert.IsAssignableFrom<INotificationService>(
            provider.GetPrimitiveService(typeof(INotificationService))
        );
        var topic = InProcessTesting.Keyspace.CreateKey("t");
        await using var stream = service
            .SubscribeAsync(topic, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        await service.PublishAsync(topic, "one"u8.ToArray(), null, cancellationToken);
        moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.Equal(1, stream.Current.Sequence);
        Assert.Equal("one"u8.ToArray(), stream.Current.Payload.ToArray());
        await provider.Connection.DropAsync(cancellationToken);

        var first = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.PublishAsync(topic, "two"u8.ToArray(), null, cancellationToken)
        );
        var second = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.PublishAsync(topic, "three"u8.ToArray(), null, cancellationToken)
        );
        var third = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.PublishAsync(topic, "four"u8.ToArray(), null, cancellationToken)
        );
        await service.PublishAsync(topic, "five"u8.ToArray(), null, cancellationToken);

        moved = await stream.MoveNextAsync();
        var resync = stream.Current;
        Assert.True(moved);
        moved = await stream.MoveNextAsync();
        var next = stream.Current;
        var payload = next.Payload.ToArray();
        Assert.True(moved);

        Assert.True(first.OutcomeUnknown);
        Assert.True(second.OutcomeUnknown);
        Assert.True(third.OutcomeUnknown);
        Assert.True(resync.IsResync);
        Assert.Equal(1, resync.Sequence);
        Assert.False(next.IsResync);
        Assert.Equal(3, next.Sequence);
        Assert.Equal("five"u8.ToArray(), payload);
    }

    #endregion
}
