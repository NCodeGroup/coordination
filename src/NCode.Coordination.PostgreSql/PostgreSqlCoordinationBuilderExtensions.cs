// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination;

/// <summary>
/// Adds the PostgreSQL provider to an application's coordination builder.
/// </summary>
public static class PostgreSqlCoordinationBuilderExtensions
{
    /// <summary>
    /// The name of the PostgreSQL provider, which a host's binding names.
    /// </summary>
    public const string ProviderName = "postgresql";

    /// <summary>
    /// The least value of <see cref="PostgreSqlProviderOptions.SweepBatchSize"/>.
    /// </summary>
    private const int MinSweepBatchSize = 1;

    /// <summary>
    /// The greatest value of <see cref="PostgreSqlProviderOptions.SweepBatchSize"/>.
    /// </summary>
    private const int MaxSweepBatchSize = 100_000;

    extension(ICoordinationBuilder builder)
    {
        /// <summary>
        /// Adds the provider that keeps every primitive in the tables of one PostgreSQL schema; the host binds it by
        /// <see cref="ProviderName"/>. The provider connects on first use.
        /// </summary>
        /// <param name="configure">Sets exactly one of <see cref="PostgreSqlProviderOptions.ConnectionString"/> and
        /// <see cref="PostgreSqlProviderOptions.DataSource"/>, and optionally the profile, the schema and the other
        /// settings.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">The builder or <paramref name="configure"/> is null, or the
        /// profile is null.</exception>
        /// <exception cref="ArgumentException">Not exactly one of the connection string and the data source is set,
        /// or the schema is not a name the provider takes.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A timeout, the sweep interval or the check interval is less
        /// than one millisecond or one hour or more, the log retention is less than one second or more than one day,
        /// or the sweep batch size is not from 1 to 100,000.</exception>
        public ICoordinationBuilder AddPostgreSqlProvider(
            Action<PostgreSqlProviderOptions> configure
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new PostgreSqlProviderOptions();
            configure(options);

            var connectionString = options.ConnectionString;
            var dataSourceFactory = options.DataSource;
            var hasConnectionString = !string.IsNullOrWhiteSpace(connectionString);
            if (hasConnectionString == (dataSourceFactory is not null))
            {
                throw new ArgumentException(
                    "Set exactly one of PostgreSqlProviderOptions.ConnectionString and PostgreSqlProviderOptions.DataSource.",
                    nameof(configure)
                );
            }

            var profile = options.Profile;
            ArgumentNullException.ThrowIfNull(profile);

            var schema = PgSchemaName.Create(options.Schema);

            var settings = CreateSettings(options, schema, nameof(configure));
            var factory = new PgProviderFactory(
                connectionString,
                dataSourceFactory,
                profile,
                settings
            );

            return builder.AddProvider(factory.Create);
        }
    }

    /// <summary>
    /// Creates the settings from the options, checking each of the options' ranges.
    /// </summary>
    /// <param name="options">The options that the host configured.</param>
    /// <param name="schema">The schema's name, already checked.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    private static PgSettings CreateSettings(
        PostgreSqlProviderOptions options,
        PgSchemaName schema,
        string paramName
    )
    {
        var named = PgSettings.Default with
        {
            Schema = schema,
            ApplyMigrations = options.ApplyMigrations,
        };
        var withTimeouts = WithServerTimeouts(named, options, paramName);

        return WithBackgroundIntervals(withTimeouts, options, paramName);
    }

    /// <summary>
    /// Sets the timeouts that bound a statement, a lock wait and an idle transaction, each checked against its range.
    /// </summary>
    /// <param name="settings">The settings to start from.</param>
    /// <param name="options">The options that the host configured.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The settings with the three timeouts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A timeout is out of range.</exception>
    private static PgSettings WithServerTimeouts(
        PgSettings settings,
        PostgreSqlProviderOptions options,
        string paramName
    )
    {
        var statementTimeout = CheckShortDuration(
            options.StatementTimeout,
            nameof(options.StatementTimeout),
            paramName
        );
        var lockTimeout = CheckShortDuration(
            options.LockTimeout,
            nameof(options.LockTimeout),
            paramName
        );
        var idleInTransactionTimeout = CheckShortDuration(
            options.IdleInTransactionTimeout,
            nameof(options.IdleInTransactionTimeout),
            paramName
        );

        return settings with
        {
            StatementTimeout = statementTimeout,
            LockTimeout = lockTimeout,
            IdleInTransactionTimeout = idleInTransactionTimeout,
        };
    }

    /// <summary>
    /// Sets the sweep's interval and batch size, the log's retention and the check interval, each checked against its
    /// range.
    /// </summary>
    /// <param name="settings">The settings to start from.</param>
    /// <param name="options">The options that the host configured.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The settings with the four values.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    private static PgSettings WithBackgroundIntervals(
        PgSettings settings,
        PostgreSqlProviderOptions options,
        string paramName
    )
    {
        var sweepInterval = CheckShortDuration(
            options.SweepInterval,
            nameof(options.SweepInterval),
            paramName
        );
        var sweepBatchSize = CheckBatchSize(options.SweepBatchSize, paramName);
        var logRetention = CheckLogRetention(options.LogRetention, paramName);
        var checkInterval = CheckShortDuration(
            options.CheckInterval,
            nameof(options.CheckInterval),
            paramName
        );

        return settings with
        {
            SweepInterval = sweepInterval,
            SweepBatchSize = sweepBatchSize,
            LogRetention = logRetention,
            CheckInterval = checkInterval,
        };
    }

    /// <summary>
    /// Checks that a duration is at least one millisecond and less than one hour.
    /// </summary>
    /// <param name="value">The duration.</param>
    /// <param name="option">The name of the option that holds it.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The duration is out of range.</exception>
    private static TimeSpan CheckShortDuration(TimeSpan value, string option, string paramName)
    {
        if (value < TimeSpan.FromMilliseconds(1) || value >= TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"PostgreSqlProviderOptions.{option} is at least one millisecond and less than one hour."
            );
        }

        return value;
    }

    /// <summary>
    /// Checks that the log retention is from one second to one day.
    /// </summary>
    /// <param name="value">The retention.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The retention.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The retention is out of range.</exception>
    private static TimeSpan CheckLogRetention(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "PostgreSqlProviderOptions.LogRetention is from one second to one day."
            );
        }

        return value;
    }

    /// <summary>
    /// Checks that the sweep batch size is from 1 to 100,000.
    /// </summary>
    /// <param name="value">The batch size.</param>
    /// <param name="paramName">The name of the argument to report.</param>
    /// <returns>The batch size.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The batch size is out of range.</exception>
    private static int CheckBatchSize(int value, string paramName)
    {
        if (value < MinSweepBatchSize || value > MaxSweepBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"PostgreSqlProviderOptions.SweepBatchSize is from {MinSweepBatchSize} to {MaxSweepBatchSize}."
            );
        }

        return value;
    }
}
