// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Runs.Tests;

public sealed class NotificationClosingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Topic { get; } = new(Space, "topic");

    private static TimeSpan ShortWait { get; } = TimeSpan.FromMilliseconds(50);

    private static async IAsyncEnumerable<Notification> StreamAsync(
        Channel<Notification> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private static HistoryOperation Closing(long invoked, long responded) =>
        HistoryTesting.Operation(
            0,
            HistoryTesting.Partition,
            new NotificationCall(NotificationCall.Last),
            invoked,
            responded,
            null
        );

    #region WaitAsync Tests

    [Fact]
    public async ValueTask WaitAsync_WhenTheClosingMessageArrives_ReturnsClosed()
    {
        var recorder = new HistoryRecorder();
        var channel = Channel.CreateUnbounded<Notification>();
        await using var subscriber = new RunSubscriber<Notification>(
            token => StreamAsync(channel, token),
            recorder,
            Token
        );
        var invoked = recorder.Stamp();
        var responded = recorder.Stamp();
        var payload = HistoryValues.Encode(NotificationCall.Last);
        await channel.Writer.WriteAsync(Notification.Message(Topic, 1, payload), Token);
        var closing = Closing(invoked, responded);

        var outcome = await NotificationClosing.WaitAsync(subscriber, closing, true, Token);

        Assert.Equal(ClosingOutcome.Closed, outcome);
    }

    [Fact]
    public async ValueTask WaitAsync_WhenAResyncArrivesAfterThePublishReturned_ReturnsClosed()
    {
        var recorder = new HistoryRecorder();
        var channel = Channel.CreateUnbounded<Notification>();
        await using var subscriber = new RunSubscriber<Notification>(
            token => StreamAsync(channel, token),
            recorder,
            Token
        );
        var invoked = recorder.Stamp();
        var responded = recorder.Stamp();
        await channel.Writer.WriteAsync(Notification.Resync(Topic, 0), Token);
        var closing = Closing(invoked, responded);

        var outcome = await NotificationClosing.WaitAsync(subscriber, closing, true, Token);

        Assert.Equal(ClosingOutcome.Closed, outcome);
    }

    [Fact]
    public async ValueTask WaitAsync_WhenAResyncArrivesWhileThePublishIsInFlight_ReturnsUnsettled()
    {
        var recorder = new HistoryRecorder();
        var channel = Channel.CreateUnbounded<Notification>();
        await using var subscriber = new RunSubscriber<Notification>(
            token => StreamAsync(channel, token),
            recorder,
            Token
        );
        var invoked = recorder.Stamp();
        await channel.Writer.WriteAsync(Notification.Resync(Topic, 0), Token);
        var read = await subscriber.WaitForAsync(_ => true, Token);
        Assert.True(read);
        var responded = recorder.Stamp();
        var closing = Closing(invoked, responded);

        var outcome = await NotificationClosing.WaitAsync(subscriber, closing, true, Token);

        Assert.Equal(ClosingOutcome.Unsettled, outcome);
    }

    [Fact]
    public async ValueTask WaitAsync_WhenNothingArrives_ReturnsMissing()
    {
        var recorder = new HistoryRecorder();
        var channel = Channel.CreateUnbounded<Notification>();
        await using var subscriber = new RunSubscriber<Notification>(
            token => StreamAsync(channel, token),
            recorder,
            ShortWait,
            Token
        );
        var invoked = recorder.Stamp();
        var responded = recorder.Stamp();
        var closing = Closing(invoked, responded);

        var outcome = await NotificationClosing.WaitAsync(subscriber, closing, true, Token);

        Assert.Equal(ClosingOutcome.Missing, outcome);
    }

    [Fact]
    public async ValueTask WaitAsync_WhenTheProviderDeclaresNoGapDetection_IgnoresAResync()
    {
        var recorder = new HistoryRecorder();
        var channel = Channel.CreateUnbounded<Notification>();
        await using var subscriber = new RunSubscriber<Notification>(
            token => StreamAsync(channel, token),
            recorder,
            ShortWait,
            Token
        );
        var invoked = recorder.Stamp();
        var responded = recorder.Stamp();
        await channel.Writer.WriteAsync(Notification.Resync(Topic, 0), Token);
        var closing = Closing(invoked, responded);

        var outcome = await NotificationClosing.WaitAsync(subscriber, closing, false, Token);

        Assert.Equal(ClosingOutcome.Missing, outcome);
    }

    #endregion
}
