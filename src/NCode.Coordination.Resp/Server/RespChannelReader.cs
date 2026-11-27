// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
internal sealed class RespChannelReader(
    ChannelReader<RespSignal> signals,
    Func<CancellationToken, ValueTask<long>> readNumber,
    TimeProvider time,
    IAsyncDisposable? feed
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
    /// Gets or sets whether the reader may have missed messages, so that the next check resyncs.
    /// </summary>
    private bool InDoubt { get; set; }

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
        catch (CoordinationException)
        {
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
        var last = await ReadNumber(cancellationToken).ConfigureAwait(false);
        var accepted = false;
        long? target = null;
        yield return RespChannelItem.ForResync(last);

        while (true)
        {
            RespSignal? next = null;
            var resync = false;
            if (target is { } expected)
            {
                if (Signals.TryRead(out var pending))
                {
                    next = pending;
                }
                else
                {
                    // Every signal that arrived within the grace has been handled.
                    target = null;
                    resync = last < expected;
                }
            }
            else
            {
                next = await Signals.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (next is { } signal)
            {
                switch (signal.Kind)
                {
                    case RespSignalKind.Message:
                        var number = ParseNumber(signal.Message);
                        if (number == last + 1)
                        {
                            last += 1;
                            accepted = true;
                            yield return RespChannelItem.ForMessage(last, signal.Message);
                        }
                        else if (number <= last && !accepted)
                        {
                            // Published before the number the last resync read.
                        }
                        else
                        {
                            resync = true;
                        }

                        break;

                    case RespSignalKind.Failed:
                        InDoubt = true;
                        break;

                    case RespSignalKind.Restored:
                        resync = true;
                        break;

                    case RespSignalKind.Check:
                        if (target is not null)
                        {
                            // A check is already waiting for the messages in flight.
                            break;
                        }

                        var current = await TryReadNumberAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (current is null)
                        {
                            InDoubt = true;
                        }
                        else if (InDoubt || current < last)
                        {
                            resync = true;
                        }
                        else if (current > last)
                        {
                            await Task.Delay(Grace, Time, cancellationToken).ConfigureAwait(false);
                            target = current;
                        }

                        break;
                }
            }

            if (!resync)
            {
                continue;
            }

            var baseline = await TryReadNumberAsync(cancellationToken).ConfigureAwait(false);
            if (baseline is null)
            {
                InDoubt = true;
                continue;
            }

            last = baseline.Value;
            accepted = false;
            target = null;
            InDoubt = false;
            yield return RespChannelItem.ForResync(last);
        }
    }
}
