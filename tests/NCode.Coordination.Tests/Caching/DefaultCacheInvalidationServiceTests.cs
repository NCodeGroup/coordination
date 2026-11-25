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
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var notifications = services.GetRequiredService<INotificationService>();
        var cache = new DefaultCacheInvalidationService(notifications);
        var topic = CreateTopic();
        var handler = new RecordingHandler();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Func<bool> hasCleared = () => handler.Entries.Contains("clear");
        Func<bool> hasInvalidated = () => handler.Entries.Contains("invalidate:0102");

        var task = cache.RunAsync(topic, handler, null, stop.Token).AsTask();

        await RecipeTesting.WaitUntilAsync(hasCleared);
        await cache.InvalidateAsync(topic, (byte[])[1, 2], null, cancellationToken);
        await RecipeTesting.WaitUntilAsync(hasInvalidated);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var entries = handler.Entries.ToArray();

        Assert.Equal(["clear", "invalidate:0102", "clear"], entries);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ClearsTheCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var notifications = services.GetRequiredService<INotificationService>();
        var cache = new DefaultCacheInvalidationService(notifications);
        var topic = CreateTopic();
        var handler = new RecordingHandler();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Func<bool> hasCleared = () => handler.Entries.Contains("clear");

        var task = cache.RunAsync(topic, handler, null, stop.Token).AsTask();

        await RecipeTesting.WaitUntilAsync(hasCleared);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var lastEntry = handler.Entries.Last();

        Assert.Equal("clear", lastEntry);
    }

    [Fact]
    public async Task RunAsync_WhenTheSubscriptionFails_ClearsTheCacheAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failing = new FailingNotifications();
        var cache = new DefaultCacheInvalidationService(failing);
        var topic = CreateTopic();
        var handler = new RecordingHandler();

        await Assert.ThrowsAsync<CoordinationException>(() =>
            cache.RunAsync(topic, handler, null, cancellationToken).AsTask()
        );
        var entries = handler.Entries.ToArray();

        Assert.Equal(["clear", "clear"], entries);
    }

    [Fact]
    public async Task RunAsync_WhenTheStreamEndsWithoutAFailure_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ending = new EndingNotifications();
        var cache = new DefaultCacheInvalidationService(ending);
        var topic = CreateTopic();
        var handler = new RecordingHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.RunAsync(topic, handler, null, cancellationToken).AsTask()
        );
        var entries = handler.Entries.ToArray();

        Assert.Equal("The subscription ended without a failure.", exception.Message);
        Assert.Equal(["clear"], entries);
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
            var hex = Convert.ToHexString(entry.Span);
            Entries.Enqueue($"invalidate:{hex}");
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            Entries.Enqueue("clear");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EndingNotifications : INotificationService
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
            await Task.Yield();

            yield break;
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
