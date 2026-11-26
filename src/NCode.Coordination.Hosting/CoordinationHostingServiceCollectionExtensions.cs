// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Coordination.Hosting.Sessions;

namespace NCode.Coordination;

/// <summary>
/// Adds the hosting of NCode.Coordination to an application's services.
/// </summary>
public static class CoordinationHostingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services that hold the host's session, once the options are bound.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The services, for chaining.</returns>
    private static IServiceCollection AddHostingServices(IServiceCollection services)
    {
        services.AddOptions<CoordinationHostOptions>().ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<CoordinationHostOptions>,
                CoordinationHostOptionsValidator
            >()
        );
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJitter, RandomJitter>();
        services.TryAddSingleton<DefaultHostSessionService>();
        services.TryAddSingleton<IHostSessionService>(x =>
            x.GetRequiredService<DefaultHostSessionService>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DefaultHostSessionService>(x =>
                x.GetRequiredService<DefaultHostSessionService>()
            )
        );

        return services;
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the host's session, which opens once the composition's requirements pass, joins the node's
        /// cluster, is renewed while the host runs and closes as the host stops, with options set in code.
        /// </summary>
        /// <param name="configure">The action that sets the node's options.</param>
        /// <returns>The services, for chaining.</returns>
        /// <remarks>
        /// The options are validated when the host starts. Call this together with the coordination
        /// composition's registration; features take the session from <see cref="IHostSessionService" />.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The services or the action are null.</exception>
        public IServiceCollection AddCoordinationHosting(Action<CoordinationHostOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<CoordinationHostOptions>().Configure(configure);

            return AddHostingServices(services);
        }

        /// <summary>
        /// Adds the host's session, which opens once the composition's requirements pass, joins the node's
        /// cluster, is renewed while the host runs and closes as the host stops, with options bound from
        /// configuration.
        /// </summary>
        /// <param name="configuration">
        /// The configuration section that holds the node's options, such as the one named
        /// <see cref="CoordinationHostOptions.SectionName" />.
        /// </param>
        /// <returns>The services, for chaining.</returns>
        /// <remarks>
        /// The binding is strict: a key that no option matches fails the host's start. The options are
        /// validated when the host starts.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The services or the configuration are null.</exception>
        public IServiceCollection AddCoordinationHosting(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services
                .AddOptions<CoordinationHostOptions>()
                .Bind(configuration, x => x.ErrorOnUnknownConfiguration = true);

            return AddHostingServices(services);
        }
    }
}
