// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// Owns the provider's listening connection and one fanout per log, and routes wake-ups, periodic checks and
/// reconnects to the fanouts.
/// </summary>
internal sealed class PgLogHub : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PgLogHub"/> class.
    /// </summary>
    /// <param name="source">The source of the listening connection.</param>
    /// <param name="keyspaces">The cache of keyspace ids, which is cleared when the server's identity changes.</param>
    /// <param name="settings">The settings of the provider.</param>
    /// <param name="time">The clock that times the checks and the listener.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public PgLogHub(PgSource source, PgKeyspaces keyspaces, PgSettings settings, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);

        Keyspaces = keyspaces ?? throw new ArgumentNullException(nameof(keyspaces));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Listener = new PgListener(source, OnNotified, OnReconnected, OnIdentityChanged, time);
    }

    /// <summary>
    /// Gets the cache of keyspace ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; }

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    private PgSettings Settings { get; }

    /// <summary>
    /// Gets the clock that times the checks.
    /// </summary>
    private TimeProvider Time { get; }

    /// <summary>
    /// Gets the listening connection.
    /// </summary>
    private PgListener Listener { get; }

    /// <summary>
    /// Gets the fanout of each log.
    /// </summary>
    private ConcurrentDictionary<PgLogId, IPgLogPump> Fanouts { get; } = new();

    /// <summary>
    /// Gets the id of the keyspace that each listened channel belongs to.
    /// </summary>
    private ConcurrentDictionary<string, long> Channels { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the gate that serializes subscribing and unsubscribing.
    /// </summary>
    private SemaphoreSlim Sync { get; } = new(1, 1);

    /// <summary>
    /// Gets the source of the token that ends the checks and the pumps.
    /// </summary>
    private CancellationTokenSource Cancellation { get; } = new();

    /// <summary>
    /// Gets the lock that guards <see cref="Disposed" /> and <see cref="Checks" />.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets or sets the task of the loop that checks every fanout, or null when it hasn't started.
    /// </summary>
    private Task? Checks { get; set; }

    /// <summary>
    /// Gets or sets a value that says whether the hub is disposed.
    /// </summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Subscribes to a log: listens on its keyspace's channel, adds a subscriber and pumps the log once.
    /// </summary>
    /// <typeparam name="TEntry">The type of the log's entries.</typeparam>
    /// <param name="id">The log.</param>
    /// <param name="create">Creates the log's fanout when no subscriber has yet.</param>
    /// <param name="after">
    /// The number after which the subscriber wants the log's entries, or null to start with a resync at the log's
    /// current number.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The subscriber, whose reader yields the log's events.</returns>
    /// <exception cref="ArgumentNullException">The create function is null.</exception>
    /// <exception cref="CoordinationException">The listener or the first read of the log failed.</exception>
    /// <exception cref="ObjectDisposedException">The hub is disposed.</exception>
    public async ValueTask<PgLogSubscriber<TEntry>> SubscribeAsync<TEntry>(
        PgLogId id,
        Func<PgLogFanout<TEntry>> create,
        long? after,
        CancellationToken cancellationToken
    )
        where TEntry : IPgLogEntry
    {
        ArgumentNullException.ThrowIfNull(create);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        PgLogFanout<TEntry> fanout;
        PgLogSubscriber<TEntry> subscriber;

        await Sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = PgChannels.ForKeyspace(Settings.Schema, id.KeyspaceId);
            if (!Channels.ContainsKey(channel))
            {
                await Listener.ListenAsync(channel, cancellationToken).ConfigureAwait(false);
                Channels[channel] = id.KeyspaceId;
            }

            if (Fanouts.TryGetValue(id, out var existing))
            {
                fanout = (PgLogFanout<TEntry>)existing;
            }
            else
            {
                fanout = create();
                Fanouts[id] = fanout;
            }

            subscriber =
                after is { } cursor && !Listener.HasSeenAnotherServer
                    ? fanout.Add(cursor, resyncFirst: false)
                    : fanout.Add(0, resyncFirst: true);
        }
        finally
        {
            Sync.Release();
        }

        StartChecks();

        try
        {
            await fanout.PumpAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UnsubscribeAsync(id, subscriber).ConfigureAwait(false);
            throw;
        }

        return subscriber;
    }

    /// <summary>
    /// Removes a subscriber, drops its log's fanout when it was the last, and stops listening to the keyspace's channel
    /// when no log of the keyspace is left.
    /// </summary>
    /// <typeparam name="TEntry">The type of the log's entries.</typeparam>
    /// <param name="id">The log.</param>
    /// <param name="subscriber">The subscriber that <see cref="SubscribeAsync{TEntry}"/> returned.</param>
    /// <returns>A task that completes when the subscriber is removed.</returns>
    /// <exception cref="ArgumentNullException">The subscriber is null.</exception>
    public async ValueTask UnsubscribeAsync<TEntry>(PgLogId id, PgLogSubscriber<TEntry> subscriber)
        where TEntry : IPgLogEntry
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        await Sync.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (
                !Fanouts.TryGetValue(id, out var existing)
                || existing is not PgLogFanout<TEntry> fanout
                || !fanout.Remove(subscriber)
            )
            {
                return;
            }

            Fanouts.TryRemove(id, out _);

            foreach (var other in Fanouts.Keys)
            {
                if (other.KeyspaceId == id.KeyspaceId)
                {
                    return;
                }
            }

            var channel = PgChannels.ForKeyspace(Settings.Schema, id.KeyspaceId);
            Channels.TryRemove(channel, out _);
            Listener.Unlisten(channel);
        }
        finally
        {
            Sync.Release();
        }
    }

    /// <summary>
    /// Finds the log that a notification wakes.
    /// </summary>
    /// <param name="channels">The ids of the keyspaces that are listened to, by their channels' names.</param>
    /// <param name="channel">The channel of the notification.</param>
    /// <param name="payload">The payload of the notification.</param>
    /// <param name="id">When this method returns true, the log.</param>
    /// <returns>True when the channel is listened to and the payload is well formed; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static bool TryRoute(
        IReadOnlyDictionary<string, long> channels,
        string channel,
        string payload,
        out PgLogId id
    )
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        id = default;

        if (!channels.TryGetValue(channel, out var keyspaceId))
        {
            return false;
        }

        if (!PgChannels.TryParseWake(payload, out var wake))
        {
            return false;
        }

        id = new PgLogId(keyspaceId, wake.TopicToken);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var checks = Stop();
        if (checks is not null)
        {
            try
            {
                await checks.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The checks ended because the hub is disposed.
            }
        }

        await Listener.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ = Stop();
        Listener.Dispose();
    }

    /// <summary>
    /// Gets a value that says whether the hub is disposed.
    /// </summary>
    private bool IsDisposed
    {
        get
        {
            lock (Gate)
            {
                return Disposed;
            }
        }
    }

    /// <summary>
    /// Marks the hub disposed and stops the checks.
    /// </summary>
    /// <returns>The checks' task, or null when they never started or the hub was disposed before.</returns>
    private Task? Stop()
    {
        lock (Gate)
        {
            if (Disposed)
            {
                return null;
            }

            Disposed = true;
        }

        // The token source is never disposed: it holds nothing to free, and a background pump may still be using its
        // token.
        Cancellation.Cancel();
        return Checks;
    }

    /// <summary>
    /// Starts the loop that pumps every fanout every check interval, once.
    /// </summary>
    private void StartChecks()
    {
        lock (Gate)
        {
            if (Disposed)
            {
                return;
            }

            Checks ??= Task.Run(() => CheckAsync(Cancellation.Token), CancellationToken.None);
        }
    }

    /// <summary>
    /// Pumps every fanout every check interval, until the hub is disposed.
    /// </summary>
    /// <param name="stop">The token that ends the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task CheckAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(Settings.CheckInterval, Time);
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                await PumpAllAsync(stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The hub is disposed.
        }
    }

    /// <summary>
    /// Pumps every fanout.
    /// </summary>
    /// <param name="stop">The token that cancels the pumps.</param>
    /// <returns>A task that completes when every pump has.</returns>
    private Task PumpAllAsync(CancellationToken stop)
    {
        var pumps = new List<Task>();
        foreach (var pump in Fanouts.Values)
        {
            pumps.Add(PumpQuietlyAsync(pump, stop));
        }

        return Task.WhenAll(pumps);
    }

    /// <summary>
    /// Pumps a fanout and swallows a failure of the server, because the next check reads again.
    /// </summary>
    /// <param name="pump">The fanout.</param>
    /// <param name="stop">The token that cancels the pump.</param>
    /// <returns>A task that completes when the pump has.</returns>
    private static async Task PumpQuietlyAsync(IPgLogPump pump, CancellationToken stop)
    {
        try
        {
            await pump.PumpAsync(stop).ConfigureAwait(false);
        }
        catch (CoordinationException)
        {
            // The next check reads again.
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The hub is disposed.
        }
    }

    /// <summary>
    /// Pumps the fanout that a notification wakes, in the background.
    /// </summary>
    /// <param name="channel">The channel of the notification.</param>
    /// <param name="payload">The payload of the notification.</param>
    private void OnNotified(string channel, string payload)
    {
        if (
            !TryRoute(Channels, channel, payload, out var id)
            || !Fanouts.TryGetValue(id, out var pump)
        )
        {
            return;
        }

        var stop = Cancellation.Token;
        _ = Task.Run(() => PumpQuietlyAsync(pump, stop), CancellationToken.None);
    }

    /// <summary>
    /// Tells every log to resync and pumps every fanout, in the background.
    /// </summary>
    private void OnReconnected()
    {
        foreach (var pump in Fanouts.Values)
        {
            pump.ResyncAll();
        }

        var stop = Cancellation.Token;
        _ = Task.Run(() => PumpAllAsync(stop), CancellationToken.None);
    }

    /// <summary>
    /// Forgets the cached keyspace ids, which another server may not hold.
    /// </summary>
    private void OnIdentityChanged()
    {
        Keyspaces.Clear();
    }
}
