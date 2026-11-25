// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Caching.Tests;

public sealed class DefaultCacheInvalidationServiceTests
{
    private static CoordinationKey CreateTopic() => RecipeTesting.Keyspace.CreateKey("topic");

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_WhenRunning_ClearsFirstThenInvalidatesEachEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var notifications = services.GetRequiredService<INotificationService>();
        var cache = new DefaultCacheInvalidationService(notifications);
        var topic = CreateTopic();
        var handler = new RecordingHandler();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var task = cache.RunAsync(topic, handler, null, stop.Token).AsTask();

        await RecipeTesting.WaitUntilAsync(() => handler.Entries.Contains("clear"));
        await cache.InvalidateAsync(topic, new byte[] { 1, 2 }, null, cancellationToken);
        await RecipeTesting.WaitUntilAsync(() => handler.Entries.Contains("invalidate:0102"));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Equal(["clear", "invalidate:0102", "clear"], handler.Entries.ToArray());
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ClearsTheCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var notifications = services.GetRequiredService<INotificationService>();
        var cache = new DefaultCacheInvalidationService(notifications);
        var handler = new RecordingHandler();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var task = cache.RunAsync(CreateTopic(), handler, null, stop.Token).AsTask();

        await RecipeTesting.WaitUntilAsync(() => handler.Entries.Contains("clear"));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Equal("clear", handler.Entries.Last());
    }

    [Fact]
    public async Task RunAsync_WhenTheSubscriptionFails_ClearsTheCacheAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cache = new DefaultCacheInvalidationService(new FailingNotifications());
        var handler = new RecordingHandler();

        await Assert.ThrowsAsync<CoordinationException>(() =>
            cache.RunAsync(CreateTopic(), handler, null, cancellationToken).AsTask()
        );

        Assert.Equal(["clear", "clear"], handler.Entries.ToArray());
    }

    #endregion

    private sealed class RecordingHandler : ICacheInvalidationHandler
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ValueTask InvalidateAsync(
            ReadOnlyMemory<byte> entry,
            CancellationToken cancellationToken
        )
        {
            Entries.Enqueue($"invalidate:{Convert.ToHexString(entry.Span)}");
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            Entries.Enqueue("clear");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingNotifications : INotificationService
    {
        public ValueTask PublishAsync(
            CoordinationKey topic,
            ReadOnlyMemory<byte> payload,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public async IAsyncEnumerable<Notification> SubscribeAsync(
            CoordinationKey topic,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            yield return Notification.Resync(topic, 0);

            await Task.Yield();

            throw new CoordinationException(
                CoordinationErrorCodes.Unavailable,
                outcomeUnknown: false,
                "The provider is unreachable."
            );
        }
    }
}
