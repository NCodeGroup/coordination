// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>A fact that runs only when <see cref="RespServers.Enabled"/> is true, and is skipped otherwise.</summary>
public sealed class RespServerFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RespServerFactAttribute"/> class.</summary>
    /// <param name="sourceFilePath">The source file of the test, filled in by the compiler.</param>
    /// <param name="sourceLineNumber">The source line of the test, filled in by the compiler.</param>
    public RespServerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    )
        : base(sourceFilePath ?? "", sourceLineNumber)
    {
        if (!RespServers.Enabled)
        {
            Skip =
                $"Set {RespServers.EnvironmentVariable}=docker to run the tests against Valkey and Garnet in Docker.";
        }
    }
}
