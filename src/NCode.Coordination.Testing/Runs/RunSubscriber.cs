// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Threading.Channels;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// Reads a subscription in the background during a run, and stamps each item it reads on the history's clock.
/// </summary>
/// <typeparam name="T">The type of the subscription's items.</typeparam>
internal sealed class RunSubscriber<T> : IAsyncDisposable
{
    /// <summary>
    /// Starts reading a subscription.
    /// </summary>
    /// <param name="subscribe">The call that opens the subscription, given a token that stops it.</param>
    /// <param name="recorder">The recorder whose clock stamps each item.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    public RunSubscriber(
        Func<CancellationToken, IAsyncEnumerable<T>> subscribe,
        HistoryRecorder recorder,
        CancellationToken cancellationToken
    )
    {
        Items = Channel.CreateUnbounded<Delivery<T>>();
        Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Received = [];

        var writer = Items.Writer;
        var token = Stop.Token;
        Reading = Task.Run(
            async () => await ReadAsync(subscribe, recorder, writer, token),
            CancellationToken.None
        );
    }

    /// <summary>
    /// Gets the channel that carries each item from the reading task to <see cref="WaitForAsync"/>.
    /// </summary>
    public Channel<Delivery<T>> Items { get; }

    /// <summary>
    /// Gets the source that stops the reading.
    /// </summary>
    public CancellationTokenSource Stop { get; }

    /// <summary>
    /// Gets the task that reads the subscription.
    /// </summary>
    public Task Reading { get; }

    /// <summary>
    /// Gets the items that <see cref="WaitForAsync"/> has taken from <see cref="Items"/>, in the order they arrived.
    /// </summary>
    public List<Delivery<T>> Received { get; }

    /// <summary>
    /// Gets a copy of <see cref="Received"/>.
    /// </summary>
    public ImmutableArray<Delivery<T>> Deliveries => [.. Received];

    /// <summary>
    /// Waits until an item that matches arrives, the subscription ends, or <see cref="ContractStreams.Timeout"/> passes.
    /// </summary>
    /// <param name="match">The predicate that tells the item that is wanted.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>True when an item matches; false when the subscription ended or the time limit passed first.</returns>
    public async ValueTask<bool> WaitForAsync(
        Func<Delivery<T>, bool> match,
        CancellationToken cancellationToken
    )
    {
        if (Received.Any(match))
        {
            return true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ContractStreams.Timeout);

        var reader = Items.Reader;

        try
        {
            while (await reader.WaitToReadAsync(timeout.Token))
            {
                while (reader.TryRead(out var delivery))
                {
                    Received.Add(delivery);

                    if (match(delivery))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the reading and waits for it to end.
    /// </summary>
    /// <returns>A task that completes when the reading has ended.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Stop.CancelAsync();
            await Reading;
        }
        finally
        {
            Stop.Dispose();
        }
    }

    /// <summary>
    /// Reads the subscription until it ends or the token cancels, writing each item to the channel with its stamp, and
    /// completes the channel.
    /// </summary>
    /// <param name="subscribe">The call that opens the subscription, given a token that stops it.</param>
    /// <param name="recorder">The recorder whose clock stamps each item.</param>
    /// <param name="writer">The writer of the channel that carries each item.</param>
    /// <param name="cancellationToken">A token that stops the reading.</param>
    /// <returns>A task that completes when the reading has ended.</returns>
    private static async ValueTask ReadAsync(
        Func<CancellationToken, IAsyncEnumerable<T>> subscribe,
        HistoryRecorder recorder,
        ChannelWriter<Delivery<T>> writer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await foreach (
                var item in subscribe(cancellationToken).WithCancellation(cancellationToken)
            )
            {
                writer.TryWrite(new Delivery<T>(recorder.Stamp(), item));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The run stopped the subscriber.
        }
        catch (CoordinationException)
        {
            // The subscription failed and its reading ends here: a provider that keeps gap detection resyncs instead.
        }
        finally
        {
            writer.Complete();
        }
    }
}
