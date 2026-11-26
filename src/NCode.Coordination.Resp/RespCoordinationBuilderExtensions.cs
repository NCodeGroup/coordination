// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination;

/// <summary>
/// Adds the RESP provider to an application's coordination builder.
/// </summary>
public static class RespCoordinationBuilderExtensions
{
    /// <summary>
    /// The name of the RESP provider, which a host's binding names.
    /// </summary>
    public const string ProviderName = "resp";

    extension(ICoordinationBuilder builder)
    {
        /// <summary>
        /// Adds the provider that keeps every primitive on a RESP server, Valkey or Garnet; the host binds it by
        /// <see cref="ProviderName"/>. The provider connects on first use.
        /// </summary>
        /// <param name="configure">Sets exactly one of <see cref="RespProviderOptions.Configuration"/> and
        /// <see cref="RespProviderOptions.Connection"/>, and optionally the profile.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">The builder or <paramref name="configure"/> is null, or the
        /// profile is null.</exception>
        /// <exception cref="ArgumentException">Not exactly one of the configuration and the connection is
        /// set.</exception>
        public ICoordinationBuilder AddRespProvider(Action<RespProviderOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new RespProviderOptions();
            configure(options);

            var configuration = options.Configuration;
            var factory = options.Connection;
            var profile = options.Profile;

            Func<IServiceProvider, RespConnection> createConnection;
            if (factory is not null && string.IsNullOrWhiteSpace(configuration))
            {
                createConnection = services => new RespConnection(
                    _ => Task.FromResult(factory(services)),
                    owned: false
                );
            }
            else if (factory is null && !string.IsNullOrWhiteSpace(configuration))
            {
                createConnection = _ => new RespConnection(
                    async ct =>
                        await ConnectionMultiplexer
                            .ConnectAsync(configuration)
                            .WaitAsync(ct)
                            .ConfigureAwait(false),
                    owned: true
                );
            }
            else
            {
                throw new ArgumentException(
                    "Set exactly one of RespProviderOptions.Configuration and RespProviderOptions.Connection.",
                    nameof(configure)
                );
            }

            ArgumentNullException.ThrowIfNull(profile);

            return builder.AddProvider(services => new RespProvider(
                createConnection(services),
                profile
            ));
        }
    }
}
