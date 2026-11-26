// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Requires the host's session on an application's coordination builder.
/// </summary>
public static class HostingCoordinationBuilderExtensions
{
    /// <summary>
    /// The name of the feature that the host's session requirements are declared for.
    /// </summary>
    public const string FeatureName = "host-session";

    /// <summary>
    /// Requires what the host's session relies on, on a keyspace or on the composition's default binding.
    /// </summary>
    /// <param name="builder">The builder to add the requirements to.</param>
    /// <param name="keyspace">The keyspace the host uses, or null for the composition's default binding.</param>
    /// <returns>The builder, for chaining.</returns>
    private static ICoordinationBuilder RequireSession(
        ICoordinationBuilder builder,
        Keyspace? keyspace
    )
    {
        // Every node must agree whether a session is live, since leases end with it.
        var session = new FeatureRequirement(
            FeatureName,
            Primitives.Session,
            Guarantees.LinearizableUntilFailover
        )
        {
            Keyspace = keyspace,
        };

        // Membership needs only a binding, as the partition-ownership recipe does.
        var membership = new FeatureRequirement(FeatureName, Primitives.Membership)
        {
            Keyspace = keyspace,
        };

        return builder.Require(session).Require(membership);
    }

    extension(ICoordinationBuilder builder)
    {
        /// <summary>
        /// Requires the primitives the host's session relies on, on the composition's default binding.
        /// </summary>
        /// <returns>The builder, for chaining.</returns>
        /// <remarks>
        /// The composition's start fails, listing each failure, when no provider is bound to the session and
        /// membership primitives or the session's provider does not keep linearizability until failover.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        public ICoordinationBuilder RequireHostSession()
        {
            ArgumentNullException.ThrowIfNull(builder);

            return RequireSession(builder, null);
        }

        /// <summary>
        /// Requires the primitives the host's session relies on, on the bindings of a keyspace.
        /// </summary>
        /// <param name="keyspace">The keyspace the host uses.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <remarks>
        /// The composition's start fails, listing each failure, when no provider is bound to the session and
        /// membership primitives for the keyspace or the session's provider does not keep linearizability until
        /// failover.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The builder or the keyspace are null.</exception>
        public ICoordinationBuilder RequireHostSession(Keyspace keyspace)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(keyspace);

            return RequireSession(builder, keyspace);
        }
    }
}
