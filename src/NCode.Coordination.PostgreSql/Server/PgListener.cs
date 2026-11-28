// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Holds the one connection of the provider that listens for <c>pg_notify</c> wake-ups: it runs <c>LISTEN</c> and
/// <c>UNLISTEN</c> between short waits, pings an idle connection, and on any failure connects again, listens again
/// and says so.
/// </summary>
/// <param name="source">The source of the connection.</param>
/// <param name="notified">Called with the channel and the payload of every notification.</param>
/// <param name="reconnected">Called after every connection but the first has listened to every referenced channel.</param>
/// <param name="identityChanged">Called when a connection reaches a server that started at another time.</param>
/// <param name="time">The clock that times the pings and the waits before a new connection.</param>
internal sealed class PgListener(
    PgSource source,
    Action<string, string> notified,
    Action reconnected,
    Action identityChanged,
    TimeProvider time
) : IAsyncDisposable, IDisposable
{
    /// <summary>The statement that reads when the server started.</summary>
    private const string StartTimeSql = "SELECT pg_postmaster_start_time()";

    /// <summary>The statement that keeps an idle connection in use.</summary>
    private const string PingSql = "SELECT 1";

    /// <summary>How long one wait for a notification lasts, between the requests the loop runs.</summary>
    private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(100);

    /// <summary>How long the connection may go without traffic before it is pinged.</summary>
    private static readonly TimeSpan PingAfter = TimeSpan.FromSeconds(15);

    /// <summary>How long the loop waits before it connects again.</summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private long _lastTraffic;
    private volatile bool _hasSeenAnotherServer;

    private PgSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    private Action<string, string> Notified { get; } =
        notified ?? throw new ArgumentNullException(nameof(notified));

    private Action Reconnected { get; } =
        reconnected ?? throw new ArgumentNullException(nameof(reconnected));

    private Action IdentityChanged { get; } =
        identityChanged ?? throw new ArgumentNullException(nameof(identityChanged));

    private TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>Gets the lock that guards the references, the requests, the loop and the disposed flag.</summary>
    private Lock Gate { get; } = new();

    /// <summary>Gets the number of references to each listened channel.</summary>
    private Dictionary<string, int> References { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the requests that the loop has yet to send.</summary>
    private Queue<PgListenerRequest> Requests { get; } = new();

    /// <summary>Gets the source of the token that ends the loop.</summary>
    private CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Gets or sets the task of the loop, or null when it hasn't started.</summary>
    private Task? Loop { get; set; }

    /// <summary>Gets or sets a value that says whether the listener is disposed.</summary>
    private bool Disposed { get; set; }

    /// <summary>Gets or sets a value that says whether a connection has listened before.</summary>
    private bool ConnectedOnce { get; set; }

    /// <summary>Gets or sets the start time that the last connection's server reported.</summary>
    private DateTime? LastStartTime { get; set; }

    /// <summary>
    /// Gets a value that says whether a connection has reached a server that started at another time than the
    /// previous connection's.
    /// </summary>
    public bool HasSeenAnotherServer => _hasSeenAnotherServer;

    /// <summary>
    /// Starts listening to a channel, or counts another reference to it.
    /// </summary>
    /// <param name="channel">The channel's name, which <see cref="PgChannels.IsChannel"/> accepts.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns>A task that completes once the server listens to the channel.</returns>
    /// <exception cref="ArgumentException">The name is not a channel of the provider.</exception>
    /// <exception cref="CoordinationException">The listener never reached the server.</exception>
    /// <exception cref="ObjectDisposedException">The listener is disposed.</exception>
    public async ValueTask ListenAsync(string channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!PgChannels.IsChannel(channel))
        {
            throw new ArgumentException(
                "The name is not a channel of the provider.",
                nameof(channel)
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            References[channel] = References.GetValueOrDefault(channel) + 1;
            Requests.Enqueue(new PgListenerRequest($"LISTEN \"{channel}\"", waiter));
            Loop ??= Task.Run(() => RunAsync(Cancellation.Token), CancellationToken.None);
        }

        try
        {
            await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Unlisten(channel);
            throw;
        }
    }

    /// <summary>
    /// Releases one reference to a channel, and stops listening to it when none is left.
    /// </summary>
    /// <param name="channel">The channel's name.</param>
    public void Unlisten(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        lock (Gate)
        {
            if (Disposed || !References.TryGetValue(channel, out var count))
            {
                return;
            }

            if (count > 1)
            {
                References[channel] = count - 1;
                return;
            }

            References.Remove(channel);
            Requests.Enqueue(new PgListenerRequest($"UNLISTEN \"{channel}\"", null));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var loop = Stop();
        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }

        Cancellation.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The loop ends by itself once it sees the cancellation, and disposes its connection; a token source that no
        // wait handle was read from holds nothing to free, so it is left for the loop, which may still be using it.
        _ = Stop();
    }

    /// <summary>
    /// Marks the listener disposed, stops the loop and fails the waiters.
    /// </summary>
    /// <returns>The loop's task, or null when it never started or the listener was disposed before.</returns>
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

        Cancellation.Cancel();
        FailWaiters(new ObjectDisposedException(nameof(PgListener)));
        return Loop;
    }

    /// <summary>
    /// Connects, serves and connects again after every failure, until the listener is disposed.
    /// </summary>
    /// <param name="stop">The token that ends the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            NpgsqlConnection? connection = null;
            try
            {
                connection = await Source.OpenAsync(stop).ConfigureAwait(false);
                connection.Notification += OnNotification;
                Touch();

                await ConnectAsync(connection, stop).ConfigureAwait(false);
                await ServeAsync(connection, stop).ConfigureAwait(false);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                if (PgFailures.IsConnectionLost(e))
                {
                    Source.ClearPool();
                }

                if (!ConnectedOnce)
                {
                    FailWaiters(Unavailable(e));
                }
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                // The listener is being disposed.
            }
            finally
            {
                if (connection is not null)
                {
                    connection.Notification -= OnNotification;
                    await DisposeQuietlyAsync(connection).ConfigureAwait(false);
                }
            }

            if (stop.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(ReconnectDelay, Time, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reads the server's identity, listens to every referenced channel and, after the first connection, says that the
    /// connection was replaced.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="stop">The token that ends the loop.</param>
    private async ValueTask ConnectAsync(NpgsqlConnection connection, CancellationToken stop)
    {
        await using (var command = new NpgsqlCommand(StartTimeSql, connection))
        {
            var result = await command.ExecuteScalarAsync(stop).ConfigureAwait(false);
            if (result is DateTime startTime)
            {
                var changed = LastStartTime is { } last && last != startTime;
                LastStartTime = startTime;
                if (changed)
                {
                    _hasSeenAnotherServer = true;
                    IdentityChanged();
                }
            }
        }

        string[] channels;
        lock (Gate)
        {
            channels = [.. References.Keys];
        }

        foreach (var channel in channels)
        {
            await ExecuteAsync(connection, $"LISTEN \"{channel}\"", stop).ConfigureAwait(false);
        }

        var wasConnected = ConnectedOnce;
        ConnectedOnce = true;
        if (wasConnected)
        {
            Reconnected();
        }
    }

    /// <summary>
    /// Runs the queued requests, waits for notifications and pings an idle connection, until a failure or disposal.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="stop">The token that ends the loop.</param>
    private async ValueTask ServeAsync(NpgsqlConnection connection, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await RunRequestsAsync(connection, stop).ConfigureAwait(false);

            if (await connection.WaitAsync(WaitSlice, stop).ConfigureAwait(false))
            {
                Touch();
            }
            else if (Time.GetElapsedTime(Volatile.Read(ref _lastTraffic)) >= PingAfter)
            {
                await ExecuteAsync(connection, PingSql, stop).ConfigureAwait(false);
                Touch();
            }
        }
    }

    /// <summary>
    /// Runs the queued requests in order and completes their waiters; a request that fails stays queued.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="stop">The token that ends the loop.</param>
    private async ValueTask RunRequestsAsync(NpgsqlConnection connection, CancellationToken stop)
    {
        while (true)
        {
            PgListenerRequest? request;
            lock (Gate)
            {
                if (!Requests.TryPeek(out request))
                {
                    return;
                }
            }

            await ExecuteAsync(connection, request.Sql, stop).ConfigureAwait(false);

            lock (Gate)
            {
                Requests.Dequeue();
            }

            request.Waiter?.TrySetResult();
        }
    }

    /// <summary>
    /// Runs one statement that returns no rows that matter.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="stop">The token that ends the loop.</param>
    private static async ValueTask ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken stop
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(stop).ConfigureAwait(false);
    }

    /// <summary>
    /// Fails every waiter and drops every queued request, which the next connection's <c>LISTEN</c>s replace.
    /// </summary>
    /// <param name="exception">The failure that every waiter receives.</param>
    private void FailWaiters(Exception exception)
    {
        PgListenerRequest[] requests;
        lock (Gate)
        {
            requests = [.. Requests];
            Requests.Clear();
        }

        foreach (var request in requests)
        {
            request.Waiter?.TrySetException(exception);
        }
    }

    /// <summary>
    /// Builds the failure that a waiter receives when the listener could not reach the server.
    /// </summary>
    /// <param name="exception">The failure of the connection.</param>
    /// <returns>An <c>unavailable</c> failure whose outcome is not unknown.</returns>
    private static CoordinationException Unavailable(Exception exception)
    {
        return PgFailures.IsClientFailure(exception)
            ? PgFailures.Translate(exception, committingStepSent: false)
            : new CoordinationException(
                CoordinationErrorCodes.Unavailable,
                false,
                "The PostgreSQL listener could not connect: " + exception.Message,
                exception
            );
    }

    /// <summary>
    /// Handles a notification of the connection.
    /// </summary>
    /// <param name="sender">The connection.</param>
    /// <param name="e">The notification.</param>
    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        Touch();
        Notified(e.Channel, e.Payload);
    }

    /// <summary>
    /// Records that the connection saw traffic now.
    /// </summary>
    private void Touch()
    {
        Volatile.Write(ref _lastTraffic, Time.GetTimestamp());
    }

    /// <summary>
    /// Disposes a connection that may be broken.
    /// </summary>
    /// <param name="connection">The connection.</param>
    private static async ValueTask DisposeQuietlyAsync(NpgsqlConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A broken connection has nothing left to release.
        }
    }
}
