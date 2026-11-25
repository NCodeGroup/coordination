// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;

namespace NCode.Coordination.InProcess.Tests;

public sealed class DroppingNotificationServiceTests : IDisposable
{
    private static async IAsyncEnumerable<Notification> StreamAsync(IEnumerable<Notification> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WhenTheInnerStreamResyncs_CarriesTheLastDeliveredSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = InProcessTesting.Keyspace.CreateKey("t");
        var payload = "one"u8.ToArray();
        var message = Notification.Message(topic, 1, payload);
        var resync = Notification.Resync(topic, 5);
        var later = Notification.Message(topic, 6, payload);
        var inner = StreamAsync([message, resync, later]);
        var mock = Mocks.Create<INotificationService>();
        mock.Setup(x => x.SubscribeAsync(topic, null, cancellationToken))
            .Returns(inner)
            .Verifiable();
        var connection = new DroppingConnection();
        var service = new DroppingNotificationService(mock.Object, connection);
        var received = new List<Notification>();

        await foreach (var item in service.SubscribeAsync(topic, null, cancellationToken))
        {
            received.Add(item);
        }

        var resyncs = received.Select(item => item.IsResync);
        var sequences = received.Select(item => item.Sequence);
        Assert.Equal<bool>([false, true, false], resyncs);
        Assert.Equal<long>([1, 1, 6], sequences);
    }

    #endregion
}
