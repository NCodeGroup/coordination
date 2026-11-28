// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Schema;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The settings of the PostgreSQL provider after its options were validated.
/// </summary>
/// <param name="Schema">The schema that holds the provider's tables.</param>
/// <param name="ApplyMigrations">Whether the provider applies the schema's migrations when it starts.</param>
/// <param name="StatementTimeout">The longest a statement may run on the server.</param>
/// <param name="LockTimeout">The longest a statement may wait for a lock.</param>
/// <param name="IdleInTransactionTimeout">The longest a session may sit idle inside a transaction.</param>
/// <param name="SweepInterval">The time between the passes that delete expired rows.</param>
/// <param name="SweepBatchSize">The most rows that a sweep deletes in one statement.</param>
/// <param name="LogRetention">How long a change log keeps its entries.</param>
/// <param name="CheckInterval">The time between the checks that read a log without a wake-up.</param>
internal sealed record PgSettings(
    PgSchemaName Schema,
    bool ApplyMigrations,
    TimeSpan StatementTimeout,
    TimeSpan LockTimeout,
    TimeSpan IdleInTransactionTimeout,
    TimeSpan SweepInterval,
    int SweepBatchSize,
    TimeSpan LogRetention,
    TimeSpan CheckInterval
)
{
    /// <summary>
    /// Gets the settings that the options' defaults give.
    /// </summary>
    public static PgSettings Default { get; } = CreateDefault();

    /// <summary>
    /// Creates the settings that the options' defaults give.
    /// </summary>
    /// <returns>The default settings.</returns>
    private static PgSettings CreateDefault()
    {
        var timeout = TimeSpan.FromSeconds(5);
        var sweepInterval = TimeSpan.FromSeconds(5);
        var logRetention = TimeSpan.FromMinutes(5);
        var checkInterval = TimeSpan.FromSeconds(5);

        return new PgSettings(
            PgSchemaName.Default,
            ApplyMigrations: true,
            StatementTimeout: timeout,
            LockTimeout: timeout,
            IdleInTransactionTimeout: timeout,
            SweepInterval: sweepInterval,
            SweepBatchSize: 1_000,
            LogRetention: logRetention,
            CheckInterval: checkInterval
        );
    }
}
