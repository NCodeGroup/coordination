// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NCode.Coordination.Composition;
using NCode.Coordination.Routing;
using NCode.Coordination.Telemetry;

namespace NCode.Coordination;

/// <summary>
/// Registers the coordination composition in an application's services.
/// </summary>
public static class CoordinationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the coordination composition, configured by the callback; calling it again adds to the same composition.
        /// </summary>
        /// <param name="configure">Registers providers, bindings, requirements and guarantees on the builder.</param>
        /// <returns>The services, for chaining.</returns>
        /// <exception cref="ArgumentNullException">The services or the callback are null.</exception>
        public IServiceCollection AddCoordination(Action<ICoordinationBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var registrations = FindRegistrations(services);
            if (registrations is null)
            {
                registrations = new CoordinationRegistrations();
                services.AddSingleton(registrations);
            }

            var builder = new DefaultCoordinationBuilder(registrations);
            configure(builder);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<CoordinationComposition>();
            services.TryAddSingleton<OperationInstrumentation>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, CoordinationStartupCheck>()
            );

            // Each primitive's service routes its calls to the bound provider.
            services.TryAddSingleton<ISessionService, DefaultSessionService>();
            services.TryAddSingleton<IMembershipService, DefaultMembershipService>();
            services.TryAddSingleton<ILeaseService, DefaultLeaseService>();
            services.TryAddSingleton<ISemaphoreService, DefaultSemaphoreService>();
            services.TryAddSingleton<IElectionService, DefaultElectionService>();
            services.TryAddSingleton<IClaimService, DefaultClaimService>();
            services.TryAddSingleton<IRecordService, DefaultRecordService>();
            services.TryAddSingleton<IRecordWatchService, DefaultRecordWatchService>();
            services.TryAddSingleton<ITransactionService, DefaultTransactionService>();
            services.TryAddSingleton<IOccurrenceService, DefaultOccurrenceService>();
            services.TryAddSingleton<ISetService, DefaultSetService>();
            services.TryAddSingleton<ICounterService, DefaultCounterService>();
            services.TryAddSingleton<ISequenceService, DefaultSequenceService>();
            services.TryAddSingleton<INotificationService, DefaultNotificationService>();
            return services;
        }
    }

    private static CoordinationRegistrations? FindRegistrations(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is CoordinationRegistrations registrations)
            {
                return registrations;
            }
        }

        return null;
    }
}
