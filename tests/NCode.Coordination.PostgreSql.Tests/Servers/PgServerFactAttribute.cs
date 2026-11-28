// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// A fact that runs only when <see cref="PgServers.Enabled"/> is true, and is skipped otherwise.
/// </summary>
public sealed class PgServerFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PgServerFactAttribute"/> class.
    /// </summary>
    /// <param name="sourceFilePath">The source file of the test, filled in by the compiler.</param>
    /// <param name="sourceLineNumber">The source line of the test, filled in by the compiler.</param>
    public PgServerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    )
        : base(sourceFilePath ?? "", sourceLineNumber)
    {
        if (!PgServers.Enabled)
        {
            Skip =
                $"Set {PgServers.EnvironmentVariable}=docker to run the tests against PostgreSQL in Docker.";
        }
    }
}
