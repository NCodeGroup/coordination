// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Composes the providers of an application, their bindings and its features' requirements; a provider's configurator
/// is an extension of it.
/// </summary>
public interface ICoordinationBuilder
{
    /// <summary>
    /// Adds a provider, created from the application's services when the composition is first needed.
    /// </summary>
    /// <param name="factory">Creates the provider from the application's services.</param>
    /// <returns>This builder, so calls chain.</returns>
    ICoordinationBuilder AddProvider(Func<IServiceProvider, ICoordinationProvider> factory);

    /// <summary>
    /// Binds a provider to primitives, for every keyspace or for one keyspace and the keyspaces under it.
    /// </summary>
    /// <param name="binding">The binding.</param>
    /// <returns>This builder, so calls chain.</returns>
    ICoordinationBuilder Bind(ProviderBinding binding);

    /// <summary>
    /// Declares what a feature requires, which the composition checks when the application starts.
    /// </summary>
    /// <param name="requirement">The requirement.</param>
    /// <returns>This builder, so calls chain.</returns>
    ICoordinationBuilder Require(FeatureRequirement requirement);

    /// <summary>
    /// Defines a guarantee the application defines beyond the built-in ones and the providers' own.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>This builder, so calls chain.</returns>
    ICoordinationBuilder Define(GuaranteeDefinition definition);

    /// <summary>
    /// Declares the application a single node: every caller is in this process, so a provider linearizable within one
    /// process meets a requirement of linearizability.
    /// </summary>
    /// <returns>This builder, so calls chain.</returns>
    ICoordinationBuilder UseSingleNode();
}
