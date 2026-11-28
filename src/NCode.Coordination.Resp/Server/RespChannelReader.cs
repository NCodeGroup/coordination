// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NCode.Coordination.Resp.Logging;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Reads a numbered channel: it yields each message numbered one above the last, and a resync at the channel's current
/// number when it starts, after a gap, a number that went backwards, a restored connection, or a periodic check that
/// found the channel ahead past the grace.
/// </summary>
/// <param name="signals">The queue of the signals that the channel's subscription and timer write.</param>
/// <param name="readNumber">Reads the channel's current number from the server.</param>
/// <param name="time">The time provider that the grace waits on.</param>
/// <param name="feed">What to dispose with the reader, or null.</param>
/// <param name="logger">The logger that reports a failed read of the channel's number.</param>
internal sealed class RespChannelReader(
    ChannelReader<RespSignal> signals,
    Func<CancellationToken, ValueTask<long>> readNumber,
    TimeProvider time,
    IAsyncDisposable? feed,
    ILogger<RespChannelReader> logger
) : IAsyncDisposable
{
    /// <summary>
    /// Gets how long a check waits for the messages in flight when it finds the channel ahead.
    /// </summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Parses the number of a message: the decimal digits before its first line feed.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The number, or null when the message has no number of 1 or more.</returns>
    public static long? ParseNumber(RedisValue message)
    {
        var bytes = (byte[]?)message;
        if (bytes is null)
        {
            return null;
        }

        var end = Array.IndexOf(bytes, (byte)'\n');
        var digits = end < 0 ? bytes : bytes.AsSpan(0, end);
        return
            long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 1
            ? number
            : null;
    }

    /// <summary>
    /// Gets the queue of the signals that the channel's subscription and timer write.
    /// </summary>
    private ChannelReader<RespSignal> Signals { get; } =
        signals ?? throw new ArgumentNullException(nameof(signals));

    /// <summary>
    /// Gets the function that reads the channel's current number from the server.
    /// </summary>
    private Func<CancellationToken, ValueTask<long>> ReadNumber { get; } =
        readNumber ?? throw new ArgumentNullException(nameof(readNumber));

    /// <summary>
    /// Gets the time provider that the grace waits on.
    /// </summary>
    private TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>
    /// Gets what is disposed with the reader, or null.
    /// </summary>
    private IAsyncDisposable? Feed { get; } = feed;

    /// <summary>
    /// Gets the logger that reports a failed read of the channel's number.
    /// </summary>
    private ILogger<RespChannelReader> Logger { get; } =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets or sets whether the reader may have missed messages, so that the next check resyncs.
    /// </summary>
    private bool InDoubt { get; set; }

    /// <summary>
    /// Gets or sets the number of the last item yielded: the last resync's or the last message's.
    /// </summary>
    private long Last { get; set; }

    /// <summary>
    /// Gets or sets whether a message was yielded since the last resync.
    /// </summary>
    private bool Accepted { get; set; }

    /// <summary>
    /// Gets or sets the number that a check found ahead while its grace is pending, or null when none is pending.
    /// </summary>
    private long? Target { get; set; }

    /// <summary>
    /// Marks the reader in doubt: the next check resyncs.
    /// </summary>
    public void MarkInDoubt() => InDoubt = true;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Feed?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Reads the channel's number, or reports that the read failed.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The number, or null when the read failed.</returns>
    private async ValueTask<long?> TryReadNumberAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReadNumber(cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException exception)
        {
            Logger.ChannelNumberReadFailed(exception);
            return null;
        }
    }

    /// <summary>
    /// Reads the channel: a resync at the channel's number, then each message and each later resync in order. The
    /// stream ends only by cancellation.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The items of the channel.</returns>
    /// <exception cref="CoordinationException">The first read of the channel's number failed.</exception>
    public async IAsyncEnumerable<RespChannelItem> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Last = await ReadNumber(cancellationToken).ConfigureAwait(false);
        yield return RespChannelItem.ForResync(Last);

        while (true)
        {
            var item = await NextItemAsync(cancellationToken).ConfigureAwait(false);
            if (item is { } next)
            {
                yield return next;
            }
        }
    }

    /// <summary>
    /// Handles the next signal, or ends a pending grace once every signal that arrived within it has been handled.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The item to yield, or null when there is none.</returns>
    private async ValueTask<RespChannelItem?> NextItemAsync(CancellationToken cancellationToken)
    {
        if (Target is not { } target)
        {
            var signal = await Signals.ReadAsync(cancellationToken).ConfigureAwait(false);
            return await HandleAsync(signal, cancellationToken).ConfigureAwait(false);
        }

        if (Signals.TryRead(out var pending))
        {
            return await HandleAsync(pending, cancellationToken).ConfigureAwait(false);
        }

        // Every signal that arrived within the grace has been handled.
        Target = null;
        if (Last >= target)
        {
            return null;
        }

        return await ResyncAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a signal: a message, a failed or restored connection, or a check.
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The item to yield, or null when there is none.</returns>
    private async ValueTask<RespChannelItem?> HandleAsync(
        RespSignal signal,
        CancellationToken cancellationToken
    )
    {
        switch (signal.Kind)
        {
            case RespSignalKind.Message:
                return await HandleMessageAsync(signal.Message, cancellationToken)
                    .ConfigureAwait(false);

            case RespSignalKind.Restored:
                return await ResyncAsync(cancellationToken).ConfigureAwait(false);

            case RespSignalKind.Check:
                return await HandleCheckAsync(cancellationToken).ConfigureAwait(false);

            case RespSignalKind.Failed:
                InDoubt = true;
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Handles a message: one numbered one above the last is delivered, one at or below the last before any message
    /// since the last resync is dropped, and any other resyncs.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The message or the resync to yield, or null when there is none.</returns>
    private async ValueTask<RespChannelItem?> HandleMessageAsync(
        RedisValue message,
        CancellationToken cancellationToken
    )
    {
        var number = ParseNumber(message);
        if (number == Last + 1)
        {
            Last += 1;
            Accepted = true;
            return RespChannelItem.ForMessage(Last, message);
        }

        if (number <= Last && !Accepted)
        {
            // Published before the number the last resync read.
            return null;
        }

        return await ResyncAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a check: it reads the channel's number and resyncs when the reader is in doubt or the number went
    /// backwards, or waits the grace when the number is ahead; a check during a pending grace is skipped.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The resync to yield, or null when there is none.</returns>
    private async ValueTask<RespChannelItem?> HandleCheckAsync(CancellationToken cancellationToken)
    {
        if (Target is not null)
        {
            // A check is already waiting for the messages in flight.
            return null;
        }

        var current = await TryReadNumberAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            InDoubt = true;
            return null;
        }

        if (InDoubt || current < Last)
        {
            return await ResyncAsync(cancellationToken).ConfigureAwait(false);
        }

        if (current > Last)
        {
            await Task.Delay(Grace, Time, cancellationToken).ConfigureAwait(false);
            Target = current;
        }

        return null;
    }

    /// <summary>
    /// Reads the channel's number and resyncs at it; when the read fails, the reader stays in doubt, so the next
    /// check resyncs.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the stream.</param>
    /// <returns>The resync to yield, or null when the read failed.</returns>
    private async ValueTask<RespChannelItem?> ResyncAsync(CancellationToken cancellationToken)
    {
        var baseline = await TryReadNumberAsync(cancellationToken).ConfigureAwait(false);
        if (baseline is not { } number)
        {
            InDoubt = true;
            return null;
        }

        Last = number;
        Accepted = false;
        Target = null;
        InDoubt = false;
        return RespChannelItem.ForResync(number);
    }
}
