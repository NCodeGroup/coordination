// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess;

namespace NCode.Coordination;

/// <summary>
/// Adds the in-process provider to an application's coordination builder.
/// </summary>
public static class InProcessCoordinationBuilderExtensions
{
    /// <summary>
    /// The name of the in-process provider, which a host's binding names.
    /// </summary>
    public const string ProviderName = "in-process";

    extension(ICoordinationBuilder builder)
    {
        /// <summary>
        /// Adds the provider that keeps every primitive in this process's memory; the host binds it by
        /// <see cref="ProviderName"/>.
        /// </summary>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        public ICoordinationBuilder AddInProcessProvider()
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.AddProvider(services =>
            {
                var time = services.GetService<TimeProvider>() ?? TimeProvider.System;

                return new InProcessProvider(time);
            });
        }
    }
}
