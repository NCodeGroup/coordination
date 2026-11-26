// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The options of the host's coordination: the cluster the node belongs to, how it appears in the cluster's
/// membership, and how its session is kept.
/// </summary>
/// <remarks>
/// Bind them from the <see cref="SectionName"/> configuration section or set them in code. The host refuses to start
/// when a value breaks its rule, and the message names each property that does.
/// </remarks>
public sealed class CoordinationHostOptions
{
    /// <summary>
    /// The configuration section the options bind from.
    /// </summary>
    public const string SectionName = "Coordination:Host";

    /// <summary>
    /// Gets or sets the name of the cluster the node belongs to: 1 to 63 lowercase letters, digits and '-', starting
    /// and ending with a letter or digit. It has no default, so the host must set it.
    /// </summary>
    public string Cluster { get; set; } = "";

    /// <summary>
    /// Gets the segments of the host's keyspace after the cluster, at most eight, each a non-empty text. None by
    /// default.
    /// </summary>
    public List<string> KeyspaceSegments { get; } = [];

    /// <summary>
    /// Gets or sets the name of the membership group the node joins, a non-empty key segment; "members" by default.
    /// </summary>
    public string Group { get; set; } = "members";

    /// <summary>
    /// Gets or sets the identity of the node, a token of 1 to 128 visible ASCII characters. It has no default, so the
    /// host must set it.
    /// </summary>
    public string NodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the address the node is reached at, such as <c>10.0.0.4:5000</c>: not blank and at most the
    /// longest address a member holds. It has no default, so the host must set it.
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// Gets the metadata the node publishes with its membership, each a provider-prefixed name with a text value.
    /// None by default.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the session's time to live: above zero and at most 366 days; 30 seconds by default.
    /// </summary>
    public TimeSpan SessionTimeToLive { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how often the host renews its session: at least one millisecond, at most one day, and shorter than the
    /// session's time to live less the drift allowance; 10 seconds by default.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the margin for clock drift by which the host treats its session as lost before its time to live
    /// ends: zero up to, not including, the session's time to live; 2 seconds by default.
    /// </summary>
    public TimeSpan DriftAllowance { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how long the host waits before it first tries again after a failed attempt to open its session or
    /// join its group; each further failure in a row doubles the wait, up to <see cref="MaxRetryDelay" />: at least one
    /// millisecond and at most one day; 2 seconds by default.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the longest the host waits between attempts to open its session or join its group, however many
    /// fail in a row; each wait is also shortened at random by up to half, so a cluster's nodes spread their
    /// attempts: at least <see cref="RetryDelay" /> and at most one day; 30 seconds by default.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
