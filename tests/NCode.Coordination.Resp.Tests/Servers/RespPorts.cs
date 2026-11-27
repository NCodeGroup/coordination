// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;

namespace NCode.Coordination.Resp.Tests.Servers;

internal static class RespPorts
{
    /// <summary>Reserves host ports that are free at the moment, for containers that publish a port as itself.</summary>
    /// <param name="count">The number of distinct ports to reserve.</param>
    /// <returns>The reserved ports.</returns>
    public static int[] Reserve(int count)
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
}
