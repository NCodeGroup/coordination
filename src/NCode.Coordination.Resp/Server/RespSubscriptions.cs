// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Opens the provider's subscriptions to numbered channels: sharded channels on a profile that carries them, plain
/// channels otherwise.
/// </summary>
/// <param name="connection">The connection of the provider to its server.</param>
/// <param name="profile">The profile of the server.</param>
/// <param name="schedule">The schedule of each subscription's periodic check.</param>
/// <exception cref="ArgumentNullException">The connection, the profile or the schedule is null.</exception>
internal class RespSubscriptions(
    RespConnection connection,
    RespProfile profile,
    RespCheckSchedule schedule
)
{
    /// <summary>
    /// Gets the interval of the periodic check when the options don't set one.
    /// </summary>
    public static TimeSpan DefaultCheckInterval { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the connection of the provider to its server.
    /// </summary>
    private RespConnection Connection { get; } =
        connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Gets the profile of the server.
    /// </summary>
    private RespProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>
    /// Gets the schedule of each subscription's periodic check.
    /// </summary>
    private RespCheckSchedule Schedule { get; } =
        schedule ?? throw new ArgumentNullException(nameof(schedule));

    /// <summary>
    /// Gets the channel with a name: a sharded channel on a profile that carries them, a plain channel otherwise.
    /// </summary>
    /// <param name="name">The name of the channel.</param>
    /// <returns>The channel.</returns>
    public RedisChannel Channel(string name) =>
        Profile.ShardedChannels ? RedisChannel.Sharded(name) : RedisChannel.Literal(name);

    /// <summary>
    /// Subscribes to a channel and opens the reader of its numbered messages; the caller disposes the reader, which
    /// disposes the subscription.
    /// </summary>
    /// <param name="name">The name of the channel.</param>
    /// <param name="readNumber">Reads the channel's current number from the server.</param>
    /// <param name="cancellationToken">
    /// A token that cancels the wait for the connection and the open before it subscribes.
    /// </param>
    /// <returns>The reader.</returns>
    /// <exception cref="ArgumentNullException">The name or the function is null.</exception>
    /// <exception cref="CoordinationException">The subscription failed.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async ValueTask<RespChannelReader> OpenAsync(
        string name,
        Func<CancellationToken, ValueTask<long>> readNumber,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(readNumber);

        var multiplexer = await Connection.GetAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Channel(name);
        RespChannelFeed feed;
        try
        {
            feed = await RespChannelFeed
                .OpenAsync(multiplexer, channel, Schedule, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (RespFailures.IsClientFailure(e))
        {
            throw RespFailures.Translate(e, isScript: false);
        }

        var time = Schedule.Time;
        return new RespChannelReader(feed.Signals, readNumber, time, feed);
    }
}
