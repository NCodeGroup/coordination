// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Holds the connection of the provider to its server. It connects on first use and retries after a failed connect.
/// It disposes the connection only when it owns it.
/// </summary>
internal sealed class RespConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the factory that opens the connection.
    /// </summary>
    private Func<CancellationToken, Task<IConnectionMultiplexer>> Connect { get; }

    /// <summary>
    /// Gets a value indicating whether this instance disposes the connection.
    /// </summary>
    private bool Owned { get; }

    // The gate is never disposed: an in-flight GetAsync still releases it, and a SemaphoreSlim whose AvailableWaitHandle is never read holds nothing to free.
    /// <summary>
    /// Gets the gate that serializes connection attempts.
    /// </summary>
    private SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    /// Gets or sets the opened connection, or null before the first successful connect.
    /// </summary>
    private IConnectionMultiplexer? Multiplexer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is disposed.
    /// </summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RespConnection"/> class.
    /// </summary>
    /// <param name="connect">Opens the connection.</param>
    /// <param name="owned">True when this instance disposes the connection, false when the host owns it.</param>
    /// <exception cref="ArgumentNullException">The factory is null.</exception>
    public RespConnection(Func<CancellationToken, Task<IConnectionMultiplexer>> connect, bool owned)
    {
        ArgumentNullException.ThrowIfNull(connect);

        Connect = connect;
        Owned = owned;
    }

    /// <summary>
    /// Returns the connection, opening it on the first call.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait for the connection.</param>
    /// <returns>The multiplexer.</returns>
    /// <exception cref="ObjectDisposedException">This instance is disposed.</exception>
    /// <exception cref="CoordinationException">The server could not be reached; the code is <c>unavailable</c> and the call did not apply.</exception>
    public async ValueTask<IConnectionMultiplexer> GetAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        var cached = Multiplexer;
        if (cached is not null)
        {
            return cached;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            cached = Multiplexer;
            if (cached is not null)
            {
                return cached;
            }

            try
            {
                cached = await Connect(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (RespFailures.IsClientFailure(e))
            {
                throw new CoordinationException(
                    CoordinationErrorCodes.Unavailable,
                    false,
                    "The RESP provider could not connect to its server: " + e.Message,
                    e
                );
            }

            Multiplexer = cached;

            if (Disposed)
            {
                if (Owned)
                {
                    await cached.DisposeAsync().ConfigureAwait(false);
                }

                throw new ObjectDisposedException(nameof(RespConnection));
            }

            return cached;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Starts a connect that this instance owns and waits for it, honoring the token. When the token cancels the
    /// wait, the connect still runs; the multiplexer it later returns is disposed so that it does not leak.
    /// </summary>
    /// <param name="start">Starts the connect.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the connect.</param>
    /// <returns>The multiplexer.</returns>
    /// <exception cref="OperationCanceledException">The token canceled the wait.</exception>
    internal static async Task<IConnectionMultiplexer> ConnectOwnedAsync(
        Func<Task<IConnectionMultiplexer>> start,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(start);

        var connecting = start();
        try
        {
            return await connecting.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = DisposeWhenConnectedAsync(connecting);
            throw;
        }
    }

    /// <summary>
    /// Waits for a connect that nobody awaits any longer and disposes the multiplexer when it succeeds.
    /// </summary>
    /// <param name="connecting">The connect.</param>
    /// <returns>A task that completes once the multiplexer is disposed or the connect has failed.</returns>
    private static async Task DisposeWhenConnectedAsync(Task<IConnectionMultiplexer> connecting)
    {
        await ((Task)connecting).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (connecting.IsCompletedSuccessfully)
        {
            var multiplexer = await connecting.ConfigureAwait(false);
            await multiplexer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;

        var multiplexer = Multiplexer;
        if (Owned && multiplexer is not null)
        {
            await multiplexer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;

        var multiplexer = Multiplexer;
        if (Owned && multiplexer is not null)
        {
            multiplexer.Dispose();
        }
    }
}
