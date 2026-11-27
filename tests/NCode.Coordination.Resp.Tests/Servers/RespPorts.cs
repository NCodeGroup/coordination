// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// Reserves free host ports for the test servers that publish a port as itself.
/// </summary>
internal static class RespPorts
{
    /// <summary>
    /// The number of times a server start tries freshly reserved ports before it fails.
    /// </summary>
    private const int StartAttempts = 3;

    /// <summary>
    /// Reserves host ports that are free at the moment, for containers that publish a port as itself.
    /// </summary>
    /// <param name="count">The number of distinct ports to reserve.</param>
    /// <returns>The reserved ports.</returns>
    public static IReadOnlyList<int> Reserve(int count)
    {
        var listeners = new List<TcpListener>(count);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var listener = new TcpListener(IPAddress.Any, 0);
                listeners.Add(listener);
                listener.Start();
            }

            return [.. listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port)];
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Dispose();
            }
        }
    }

    /// <summary>
    /// Starts a server on freshly reserved host ports, and on new ones when the start fails, since another container
    /// can take a port between its reservation and the bind.
    /// </summary>
    /// <typeparam name="T">The type of the started server.</typeparam>
    /// <param name="count">The number of distinct ports to reserve for each try.</param>
    /// <param name="start">The start that takes the reserved ports and disposes what it built when it fails.</param>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started server.</returns>
    public static async ValueTask<T> StartOnFreePortsAsync<T>(
        int count,
        Func<IReadOnlyList<int>, CancellationToken, ValueTask<T>> start,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            var ports = Reserve(count);
            try
            {
                return await start(ports, cancellationToken);
            }
            catch (Exception exception)
                when (attempt < StartAttempts && exception is not OperationCanceledException)
            {
                // Try again on ports reserved afresh; the start disposed what it built.
            }
        }
    }
}
