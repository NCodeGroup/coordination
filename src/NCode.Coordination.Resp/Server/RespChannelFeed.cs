// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Feeds a channel reader: one subscription to a server channel, a timer for the periodic check and the connection's
/// events, each written as a signal to a queue of at most <see cref="Capacity" /> signals.
/// </summary>
/// <param name="queue">The queue of the signals.</param>
/// <param name="subscription">The subscription to the server channel.</param>
/// <param name="events">The signals of the connection's events.</param>
/// <param name="timer">The timer of the periodic check.</param>
/// <exception cref="ArgumentNullException">The queue, the subscription, the events or the timer is null.</exception>
internal sealed class RespChannelFeed(
    Channel<RespSignal> queue,
    ChannelMessageQueue subscription,
    RespConnectionSignals events,
    ITimer timer
) : IAsyncDisposable
{
    /// <summary>
    /// The most signals that the queue holds; a signal that finds it full is dropped.
    /// </summary>
    public const int Capacity = 1_024;

    /// <summary>
    /// Subscribes to a channel and starts feeding its signals.
    /// </summary>
    /// <param name="multiplexer">The client.</param>
    /// <param name="channel">The channel to subscribe to.</param>
    /// <param name="schedule">The schedule of the periodic check.</param>
    /// <param name="cancellationToken">A token that cancels the open before it subscribes.</param>
    /// <returns>The feed, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">The client or the schedule is null.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled before the subscribe.</exception>
    public static async ValueTask<RespChannelFeed> OpenAsync(
        IConnectionMultiplexer multiplexer,
        RedisChannel channel,
        RespCheckSchedule schedule,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(schedule);

        // The token isn't passed to the subscribe: a subscribe abandoned in flight would leave a subscription and its
        // handlers that nobody disposes.
        cancellationToken.ThrowIfCancellationRequested();

        var options = new BoundedChannelOptions(Capacity) { SingleReader = true };
        var signals = Channel.CreateBounded<RespSignal>(options);
        var writer = signals.Writer;

        var events = new RespConnectionSignals(multiplexer, writer);
        ChannelMessageQueue queue;
        try
        {
            var subscriber = multiplexer.GetSubscriber();
            queue = await subscriber.SubscribeAsync(channel).ConfigureAwait(false);
        }
        catch
        {
            events.Dispose();
            throw;
        }

        queue.OnMessage(message =>
        {
            var signal = RespSignal.ForMessage(message.Message);
            _ = writer.TryWrite(signal);
        });

        var time = schedule.Time;
        var interval = schedule.Interval;
        var timer = time.CreateTimer(
            _ => writer.TryWrite(RespSignal.Check),
            null,
            interval,
            interval
        );

        return new RespChannelFeed(signals, queue, events, timer);
    }

    /// <summary>
    /// Gets the queue of the signals.
    /// </summary>
    private Channel<RespSignal> Queue { get; } =
        queue ?? throw new ArgumentNullException(nameof(queue));

    /// <summary>
    /// Gets the subscription to the server channel.
    /// </summary>
    private ChannelMessageQueue Subscription { get; } =
        subscription ?? throw new ArgumentNullException(nameof(subscription));

    /// <summary>
    /// Gets the signals of the connection's events.
    /// </summary>
    private RespConnectionSignals Events { get; } =
        events ?? throw new ArgumentNullException(nameof(events));

    /// <summary>
    /// Gets the timer of the periodic check.
    /// </summary>
    private ITimer Timer { get; } = timer ?? throw new ArgumentNullException(nameof(timer));

    /// <summary>
    /// Gets the reader of the queue of the signals.
    /// </summary>
    public ChannelReader<RespSignal> Signals => Queue.Reader;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Timer.DisposeAsync().ConfigureAwait(false);
        Events.Dispose();
        _ = await TryUnsubscribeAsync().ConfigureAwait(false);

        _ = Queue.Writer.TryComplete();
    }

    // No token: DisposeAsync takes none, and the unsubscribe can't be canceled.
    /// <summary>
    /// Unsubscribes from the server channel. A failed or disposed connection is not an error here: the subscription
    /// ends with the connection.
    /// </summary>
    /// <returns>True when the unsubscribe completed; false when the connection failed or was disposed.</returns>
    private async ValueTask<bool> TryUnsubscribeAsync()
    {
        try
        {
            await Subscription.UnsubscribeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (RespFailures.IsClientFailure(e) || e is ObjectDisposedException)
        {
            return false;
        }
    }
}
