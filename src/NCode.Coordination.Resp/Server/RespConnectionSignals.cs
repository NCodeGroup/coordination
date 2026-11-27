// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Writes a signal for each failure and each restore of the client's connections, until disposed.
/// </summary>
internal sealed class RespConnectionSignals : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RespConnectionSignals" /> class.
    /// </summary>
    /// <param name="multiplexer">The client whose connection events are signaled.</param>
    /// <param name="writer">The writer that receives the signals.</param>
    public RespConnectionSignals(
        IConnectionMultiplexer multiplexer,
        ChannelWriter<RespSignal> writer
    )
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(writer);

        Multiplexer = multiplexer;
        Writer = writer;

        multiplexer.ConnectionFailed += OnConnectionFailed;
        multiplexer.ConnectionRestored += OnConnectionRestored;
    }

    /// <summary>
    /// Gets the client whose connection events are signaled.
    /// </summary>
    private IConnectionMultiplexer Multiplexer { get; }

    /// <summary>
    /// Gets the writer that receives the signals.
    /// </summary>
    private ChannelWriter<RespSignal> Writer { get; }

    /// <summary>
    /// Gets or sets a value that indicates whether the handlers were detached.
    /// </summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Writes the signal for a failed connection.
    /// </summary>
    /// <param name="sender">The client.</param>
    /// <param name="e">The event data.</param>
    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e) =>
        _ = Writer.TryWrite(RespSignal.Failed);

    /// <summary>
    /// Writes the signal for a restored connection.
    /// </summary>
    /// <param name="sender">The client.</param>
    /// <param name="e">The event data.</param>
    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e) =>
        _ = Writer.TryWrite(RespSignal.Restored);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;
        Multiplexer.ConnectionFailed -= OnConnectionFailed;
        Multiplexer.ConnectionRestored -= OnConnectionRestored;
    }
}
