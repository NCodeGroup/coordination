// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// The deployment a PostgreSQL provider declares: the durability it claims and whether its probe requires a
/// synchronous standby.
/// </summary>
/// <remarks>
/// Two profiles are equal when all their values are equal. A name is lowercase letters and digits, with a single '.'
/// or '-' between them, and at most 128 characters.
/// </remarks>
public sealed partial record PostgreSqlProfile
{
    /// <summary>The longest profile name, in characters.</summary>
    private const int MaxNameLength = 128;

    /// <summary>The prefix of the name of every durability guarantee.</summary>
    private const string DurabilityPrefix = "durability.";

    /// <summary>Matches a name: lowercase letters and digits, with a single '.' or '-' between them.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// Creates a profile.
    /// </summary>
    /// <param name="name">
    /// The name: lowercase letters and digits, with a single '.' or '-' between them, at most 128 characters.
    /// </param>
    /// <param name="durability">The durability guarantee that the provider declares, whose name starts with <c>durability.</c>.</param>
    /// <param name="requiresSynchronousStandby">
    /// Whether the probe requires <c>synchronous_standby_names</c> to name a standby and <c>synchronous_commit</c> to
    /// be <c>on</c> or <c>remote_apply</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">The name or the durability is null.</exception>
    /// <exception cref="ArgumentException">
    /// The name is not a valid name, or the durability is not a durability guarantee.
    /// </exception>
    public PostgreSqlProfile(string name, Guarantee durability, bool requiresSynchronousStandby)
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

        ArgumentNullException.ThrowIfNull(durability);

        if (!durability.Name.StartsWith(DurabilityPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{durability.Name}' is not a durability guarantee: its name starts with '{DurabilityPrefix}'.",
                nameof(durability)
            );
        }

        Name = name;
        Durability = durability;
        RequiresSynchronousStandby = requiresSynchronousStandby;
    }

    /// <summary>
    /// Gets the name of the profile.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the durability guarantee that the provider declares.
    /// </summary>
    public Guarantee Durability { get; }

    /// <summary>
    /// Gets a value that says whether the probe requires a synchronous standby.
    /// </summary>
    public bool RequiresSynchronousStandby { get; }

    /// <summary>
    /// Returns the name of the profile.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
