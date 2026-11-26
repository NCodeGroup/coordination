// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// The values of <see cref="CoordinationHostOptions"/> once every rule has passed, in the contracts' own types.
/// </summary>
internal sealed class HostSessionSettings
{
    /// <summary>
    /// The text a check puts in the fields of a member and its session that it doesn't check.
    /// </summary>
    private const string PlaceholderText = "validation";

    /// <summary>
    /// Checks the options against the rules the contracts enforce and builds the settings from them.
    /// </summary>
    /// <param name="options">The options to check.</param>
    /// <returns>The settings when every rule passed, otherwise one failure for each property that broke its rule.</returns>
    /// <exception cref="ArgumentNullException">The options are null.</exception>
    public static HostSessionSettingsResult Create(CoordinationHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        var keyspace = CheckKeyspace(options, failures);

        var group = Check(failures, nameof(CoordinationHostOptions.Group), () =>
            KeySegment.FromString(options.Group)
        );

        var member = CheckMember(options, failures);

        var timeToLive = CheckPeriods(options, failures);

        if (
            failures.Count != 0
            || keyspace is null
            || group is null
            || member is null
            || timeToLive is null
        )
        {
            return new HostSessionSettingsResult { Failures = failures };
        }

        var settings = new HostSessionSettings
        {
            Keyspace = keyspace,
            Group = keyspace.CreateKey(group),
            NodeId = member.NodeId,
            Address = member.Address,
            Metadata = member.Metadata,
            TimeToLive = timeToLive,
            DriftAllowance = options.DriftAllowance,
            KeepAliveInterval = options.KeepAliveInterval,
            RetryDelay = options.RetryDelay,
            MaxRetryDelay = options.MaxRetryDelay,
        };

        return new HostSessionSettingsResult { Settings = settings, Failures = [] };
    }

    /// <summary>
    /// Checks the cluster and the keyspace segments, which build the keyspace the host's session and group live in.
    /// </summary>
    /// <param name="options">The options to check.</param>
    /// <param name="failures">Receives one failure for each property that breaks its rule.</param>
    /// <returns>The keyspace, or null when a property failed.</returns>
    private static Keyspace? CheckKeyspace(CoordinationHostOptions options, List<string> failures)
    {
        var keyspace = Check(failures, nameof(CoordinationHostOptions.Cluster), () =>
            new Keyspace(options.Cluster)
        );

        if (keyspace is not null)
        {
            keyspace = Check(failures, nameof(CoordinationHostOptions.KeyspaceSegments), () =>
            {
                var segments = options.KeyspaceSegments.Select(KeySegment.FromString);
                return new Keyspace(options.Cluster, segments);
            });
        }

        return keyspace;
    }

    /// <summary>
    /// Checks the node id, the address and the metadata, which build the member the node joins its group as.
    /// </summary>
    /// <param name="options">The options to check.</param>
    /// <param name="failures">Receives one failure for each property that breaks its rule.</param>
    /// <returns>A member with the node's identity, address and metadata, or null when a property failed.</returns>
    private static Member? CheckMember(CoordinationHostOptions options, List<string> failures)
    {
        var nodeId = Check(failures, nameof(CoordinationHostOptions.NodeId), () =>
            new Member(Placeholder, options.NodeId, PlaceholderText).NodeId
        );

        var address = Check(failures, nameof(CoordinationHostOptions.Address), () =>
            new Member(Placeholder, PlaceholderText, options.Address).Address
        );

        var metadata = Check(failures, nameof(CoordinationHostOptions.Metadata), () =>
        {
            var entries = ImmutableDictionary.CreateRange(StringComparer.Ordinal, options.Metadata);
            return new Member(Placeholder, PlaceholderText, PlaceholderText)
            {
                Metadata = entries,
            }.Metadata;
        });

        if (nodeId is null || address is null || metadata is null)
        {
            return null;
        }

        return new Member(Placeholder, nodeId, address) { Metadata = metadata };
    }

    /// <summary>
    /// Checks the session's time to live, the drift allowance, the keep-alive interval, the retry delay and the longest
    /// retry delay, which depend on one another.
    /// </summary>
    /// <param name="options">The options to check.</param>
    /// <param name="failures">Receives one failure for each property that breaks its rule.</param>
    /// <returns>The time to live, or null when it failed; a period that failed leaves a failure.</returns>
    private static TimeToLive? CheckPeriods(CoordinationHostOptions options, List<string> failures)
    {
        var timeToLive = Check(failures, nameof(CoordinationHostOptions.SessionTimeToLive), () =>
            new TimeToLive(options.SessionTimeToLive)
        );

        HolderDeadline? deadline = null;

        if (timeToLive is not null)
        {
            deadline = Check(failures, nameof(CoordinationHostOptions.DriftAllowance), () =>
                HolderDeadline.Begin(TimeProvider.System, timeToLive, options.DriftAllowance)
            );
        }

        var keepAliveInterval = options.KeepAliveInterval;

        if (
            keepAliveInterval < MinInterval
            || keepAliveInterval > MaxInterval
            || (deadline is not null && keepAliveInterval >= deadline.Lifetime)
        )
        {
            failures.Add(
                $"{nameof(CoordinationHostOptions.KeepAliveInterval)}: the interval is at least {MinInterval}, at most {MaxInterval}, and shorter than {nameof(CoordinationHostOptions.SessionTimeToLive)} less {nameof(CoordinationHostOptions.DriftAllowance)}."
            );
        }

        var retryDelay = options.RetryDelay;

        if (retryDelay < MinInterval || retryDelay > MaxInterval)
        {
            failures.Add(
                $"{nameof(CoordinationHostOptions.RetryDelay)}: the delay is at least {MinInterval} and at most {MaxInterval}."
            );
        }

        var maxRetryDelay = options.MaxRetryDelay;

        if (
            maxRetryDelay < MinInterval
            || maxRetryDelay < retryDelay
            || maxRetryDelay > MaxInterval
        )
        {
            failures.Add(
                $"{nameof(CoordinationHostOptions.MaxRetryDelay)}: the delay is at least {MinInterval}, at least {nameof(CoordinationHostOptions.RetryDelay)}, and at most {MaxInterval}."
            );
        }

        return timeToLive;
    }

    /// <summary>
    /// Runs one of the contracts' constructors and records its complaint as a failure of one property.
    /// </summary>
    /// <typeparam name="T">The type the constructor builds.</typeparam>
    /// <param name="failures">Receives the failure.</param>
    /// <param name="key">The name of the property that is checked.</param>
    /// <param name="create">Builds the value from the property.</param>
    /// <returns>The value, or null when the constructor refused the property.</returns>
    private static T? Check<T>(List<string> failures, string key, Func<T> create)
        where T : class
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            failures.Add($"{key}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets a session id the contracts' constructors need to build a member for a check.
    /// </summary>
    private static SessionId Placeholder { get; } =
        new(new Keyspace(PlaceholderText), PlaceholderText);

    /// <summary>
    /// Gets the shortest keep-alive interval, retry delay and longest retry delay, one millisecond, the shortest period a
    /// timer accepts.
    /// </summary>
    public static TimeSpan MinInterval { get; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Gets the longest keep-alive interval, retry delay and longest retry delay, one day. A timer refuses a period of
    /// about 49 days, so a smaller bound keeps every accepted value well inside it.
    /// </summary>
    public static TimeSpan MaxInterval { get; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets the keyspace the host's session and group live in.
    /// </summary>
    public required Keyspace Keyspace { get; init; }

    /// <summary>
    /// Gets the key of the membership group the node joins.
    /// </summary>
    public required CoordinationKey Group { get; init; }

    /// <summary>
    /// Gets the identity of the node.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Gets the address the node is reached at.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Gets the metadata the node publishes with its membership.
    /// </summary>
    public required ImmutableDictionary<string, string> Metadata { get; init; }

    /// <summary>
    /// Gets the time to live the session is opened with.
    /// </summary>
    public required TimeToLive TimeToLive { get; init; }

    /// <summary>
    /// Gets the margin for clock drift by which the host's deadline passes before the time to live ends.
    /// </summary>
    public required TimeSpan DriftAllowance { get; init; }

    /// <summary>
    /// Gets how often the host renews its session.
    /// </summary>
    public required TimeSpan KeepAliveInterval { get; init; }

    /// <summary>
    /// Gets how long the host waits before it tries again after a failed call.
    /// </summary>
    public required TimeSpan RetryDelay { get; init; }

    /// <summary>
    /// Gets the longest the host waits between attempts to open its session or join its group.
    /// </summary>
    public required TimeSpan MaxRetryDelay { get; init; }

    /// <summary>
    /// Creates the member the node joins its group as.
    /// </summary>
    /// <param name="session">The session that keeps the member alive.</param>
    /// <returns>The member, with the node's identity, address and metadata.</returns>
    public Member CreateMember(SessionId session) =>
        new(session, NodeId, Address) { Metadata = Metadata };

    /// <summary>
    /// Gets how long the host waits before its next attempt after some failed attempts in a row. The wait starts at
    /// the retry delay, doubles with each further failure up to the longest retry delay, and is shortened by the
    /// jitter, up to half of it.
    /// </summary>
    /// <param name="failures">The number of failed attempts in a row, at least one.</param>
    /// <param name="jitter">The random fraction, zero up to, not including, one, that shortens the wait.</param>
    /// <returns>The wait, more than half of the doubled delay and at most the doubled delay.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The failures are below one, or the jitter is not a number from zero up to, not including, one.
    /// </exception>
    public TimeSpan GetRetryDelay(int failures, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failures, 1);

        if (double.IsNaN(jitter) || jitter < 0 || jitter >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitter),
                jitter,
                "The jitter is from zero up to, not including, one."
            );
        }

        var ceiling = RetryDelay;

        for (var attempt = 1; attempt < failures && ceiling < MaxRetryDelay; attempt++)
        {
            var doubled = ceiling * 2;
            ceiling = doubled < MaxRetryDelay ? doubled : MaxRetryDelay;
        }

        return ceiling - (ceiling * (jitter / 2));
    }
}
