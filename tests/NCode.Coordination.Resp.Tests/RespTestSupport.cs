// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Helpers that the RESP provider's restart, failover and cluster tests share.
/// </summary>
internal static class RespTestSupport
{
    /// <summary>
    /// Gets how long a test waits for a server to answer again.
    /// </summary>
    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a test waits for the next item of a stream.
    /// </summary>
    private static TimeSpan NextLimit { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Creates a keyspace with a unique id, so that no other test sees its keys.
    /// </summary>
    /// <param name="name">The name of the keyspace, which tells the tests' keys apart.</param>
    /// <returns>The keyspace.</returns>
    public static Keyspace CreateKeyspace(string name)
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace(name, segment);
    }

    /// <summary>
    /// Gets a primitive service of the provider, failing the test when the provider offers none.
    /// </summary>
    /// <typeparam name="TService">The service interface to get.</typeparam>
    /// <param name="provider">The provider that offers the service.</param>
    /// <returns>The service.</returns>
    public static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    /// <summary>
    /// Moves an enumerator to its next item, failing the test when none arrives in time.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="enumerator">The enumerator to move.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The next item.</returns>
    public static async ValueTask<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator,
        CancellationToken cancellationToken
    )
    {
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(NextLimit, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    /// <summary>
    /// Repeats a call until it stops failing with a coordination exception, which is how a client sees a server that
    /// has not yet come back or become the primary, and fails the test when a failure isn't unavailable or the server
    /// stays silent for too long.
    /// </summary>
    /// <param name="call">The call to repeat.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the call succeeds.</returns>
    public static async ValueTask WaitUntilAnsweredAsync(
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var pause = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try
            {
                await call(cancellationToken);
                return;
            }
            catch (CoordinationException exception)
            {
                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                if (stopwatch.Elapsed > WaitLimit)
                    Assert.Fail(
                        $"The server did not answer within {WaitLimit}: {exception.Message}"
                    );

                await Task.Delay(pause, cancellationToken);
            }
        }
    }
}
