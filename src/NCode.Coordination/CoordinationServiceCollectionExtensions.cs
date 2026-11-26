// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NCode.Coordination.Barriers;
using NCode.Coordination.Caching;
using NCode.Coordination.Composition;
using NCode.Coordination.Locks;
using NCode.Coordination.Partitions;
using NCode.Coordination.RateLimiting;
using NCode.Coordination.Routing;
using NCode.Coordination.SingleFlight;
using NCode.Coordination.Singletons;
using NCode.Coordination.Telemetry;

namespace NCode.Coordination;

/// <summary>
/// Registers the coordination composition in an application's services.
/// </summary>
public static class CoordinationServiceCollectionExtensions
{
    /// <summary>
    /// Finds the registrations a previous call added to the services.
    /// </summary>
    /// <param name="services">The services to search.</param>
    /// <returns>The registrations, or null when none were added.</returns>
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

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the coordination composition and its recipes, configured by the callback; calling it again adds to
        /// the same composition.
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
            var startupCheck = ServiceDescriptor.Singleton<
                IHostedService,
                CoordinationStartupCheck
            >();
            services.TryAddEnumerable(startupCheck);

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

            // Each recipe calls the routed primitive services.
            services.TryAddSingleton<ILockService, DefaultLockService>();
            services.TryAddSingleton<IReadWriteLockService, DefaultReadWriteLockService>();
            services.TryAddSingleton<IBarrierService, DefaultBarrierService>();
            services.TryAddSingleton<ISingletonService, DefaultSingletonService>();
            services.TryAddSingleton<
                IPartitionOwnershipService,
                DefaultPartitionOwnershipService
            >();
            services.TryAddSingleton<ISingleFlightService, DefaultSingleFlightService>();
            services.TryAddSingleton<IRateLimiterFactory, DefaultRateLimiterFactory>();
            services.TryAddSingleton<ICacheInvalidationService, DefaultCacheInvalidationService>();
            return services;
        }
    }
}
