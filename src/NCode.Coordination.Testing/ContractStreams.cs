// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The helpers that the contract suites use to read watches and subscriptions, and to wait for a task, within a time
/// limit.
/// </summary>
internal static class ContractStreams
{
    /// <summary>
    /// The most items that <see cref="NextMatchingAsync{T}"/> reads before it fails.
    /// </summary>
    private const int MaxItemsRead = 1_000;

    /// <summary>
    /// Gets how long a test waits for one item of a stream, or for a task, before it fails.
    /// </summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts reading a stream with the current test's cancellation token.
    /// </summary>
    /// <typeparam name="T">The type of the stream's items.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The enumerator that reads the stream.</returns>
    public static IAsyncEnumerator<T> Open<T>(IAsyncEnumerable<T> stream) =>
        stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

    /// <summary>
    /// Reads the next item of a stream, and fails when none arrives within <see cref="Timeout"/> or the stream ends.
    /// </summary>
    /// <typeparam name="T">The type of the stream's items.</typeparam>
    /// <param name="stream">The enumerator that reads the stream.</param>
    /// <returns>The next item.</returns>
    public static async ValueTask<T> NextAsync<T>(IAsyncEnumerator<T> stream)
    {
        var moved = await stream
            .MoveNextAsync()
            .AsTask()
            .WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(moved, "The stream ended.");
        return stream.Current;
    }

    /// <summary>
    /// Reads items of a stream until one matches, and fails when none of the next <see cref="MaxItemsRead"/> items
    /// matches.
    /// </summary>
    /// <typeparam name="T">The type of the stream's items.</typeparam>
    /// <param name="stream">The enumerator that reads the stream.</param>
    /// <param name="match">The predicate that tells the item that is wanted.</param>
    /// <returns>The first item that matches.</returns>
    public static async ValueTask<T> NextMatchingAsync<T>(
        IAsyncEnumerator<T> stream,
        Func<T, bool> match
    )
    {
        for (var index = 0; index < MaxItemsRead; index++)
        {
            var item = await NextAsync(stream);

            if (match(item))
            {
                return item;
            }
        }

        Assert.Fail("No matching item arrived.");
        throw new UnreachableException();
    }

    /// <summary>
    /// Waits for a task to complete, and fails when it does not within <see cref="Timeout"/>.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    /// <returns>A task that completes when <paramref name="task"/> has completed.</returns>
    public static async ValueTask CompletesAsync(Task task) =>
        await task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
}
