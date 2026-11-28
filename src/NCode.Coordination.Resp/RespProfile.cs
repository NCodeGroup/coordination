// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// The vendor a RESP provider expects: the server it must find, the oldest version it accepts and how it carries
/// channel messages.
/// </summary>
/// <remarks>
/// Two profiles are equal when all their values are equal. A name is lowercase letters and digits, with a single '.'
/// or '-' between them, and at most 128 characters.
/// </remarks>
public sealed partial record RespProfile
{
    /// <summary>The longest profile name, in characters.</summary>
    private const int MaxNameLength = 128;

    /// <summary>Matches a name: lowercase letters and digits, with a single '.' or '-' between them.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// Creates a profile.
    /// </summary>
    /// <param name="name">
    /// The name: lowercase letters and digits, with a single '.' or '-' between them, at most 128 characters.
    /// </param>
    /// <param name="serverName">The <c>server_name</c> that <c>INFO server</c> must report.</param>
    /// <param name="versionField">The <c>INFO server</c> field that holds the server's version.</param>
    /// <param name="minimumVersion">The oldest version the provider accepts.</param>
    /// <param name="shardedChannels">Whether the server carries channel messages on sharded channels.</param>
    /// <exception cref="ArgumentNullException">The name or the minimum version is null.</exception>
    /// <exception cref="ArgumentException">
    /// The name is not a valid name, or the server name or the version field is null, empty or white space.
    /// </exception>
    public RespProfile(
        string name,
        string serverName,
        string versionField,
        Version minimumVersion,
        bool shardedChannels
    )
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"A name is at most {MaxNameLength} characters, and this one has {name.Length}.",
                nameof(name)
            );
        }

        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a name: use lowercase letters and digits, with a single '.' or '-' between them.",
                nameof(name)
            );
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionField);
        ArgumentNullException.ThrowIfNull(minimumVersion);

        Name = name;
        ServerName = serverName;
        VersionField = versionField;
        MinimumVersion = minimumVersion;
        ShardedChannels = shardedChannels;
    }

    /// <summary>
    /// Gets the name of the profile.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the <c>server_name</c> that <c>INFO server</c> must report.
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// Gets the <c>INFO server</c> field that holds the server's version.
    /// </summary>
    public string VersionField { get; }

    /// <summary>
    /// Gets the oldest version the provider accepts.
    /// </summary>
    public Version MinimumVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the server carries channel messages on sharded channels.
    /// </summary>
    public bool ShardedChannels { get; }

    /// <summary>
    /// Returns the name of the profile.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
