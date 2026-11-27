// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The profiles that the PostgreSQL provider supports.
/// </summary>
/// <remarks>
/// A profile declares a durability that the deployment must make true: the probe checks what it can, and the host
/// chooses the profile that its deployment satisfies.
/// </remarks>
public static class PostgreSqlProfiles
{
    // ADR-1031 rule 8.
    /// <summary>
    /// Gets the profile for a single server whose commits are flushed to disk.
    /// </summary>
    /// <remarks>
    /// It declares <c>durability.durable</c> and requires no standby, so a write survives a crash of the server but
    /// not the loss of its disk. The probe still refuses a server that runs with <c>fsync</c> off or whose sessions
    /// see <c>synchronous_commit</c> off.
    /// </remarks>
    public static PostgreSqlProfile Standalone { get; } =
        new("postgresql-standalone", Guarantees.Durable, requiresSynchronousStandby: false);

    // ADR-1031 rule 8.
    /// <summary>
    /// Gets the profile for a primary with standbys that copy its commits after it acknowledges them.
    /// </summary>
    /// <remarks>
    /// It declares <c>durability.replicated-asynchronously</c> and requires no standby: the probe refuses a server
    /// that runs with <c>fsync</c> off or whose sessions see <c>synchronous_commit</c> off, but a failover may lose
    /// commits the standby had not received. This is the default profile.
    /// </remarks>
    public static PostgreSqlProfile Asynchronous { get; } =
        new(
            "postgresql-asynchronous",
            Guarantees.ReplicatedAsynchronously,
            requiresSynchronousStandby: false
        );

    // ADR-1031 rule 8.
    /// <summary>
    /// Gets the profile for a primary that waits for a synchronous standby before it acknowledges a commit.
    /// </summary>
    /// <remarks>
    /// It declares <c>durability.durable</c> and requires <c>synchronous_standby_names</c> to name a standby and
    /// <c>synchronous_commit</c> to be <c>on</c> or <c>remote_apply</c> in the provider's sessions; the probe refuses
    /// a server where either is not so.
    /// </remarks>
    public static PostgreSqlProfile Synchronous { get; } =
        new("postgresql-synchronous", Guarantees.Durable, requiresSynchronousStandby: true);
}
