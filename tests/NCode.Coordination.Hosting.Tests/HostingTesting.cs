// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.Hosting.Sessions;

namespace NCode.Coordination.Hosting.Tests;

/// <summary>
/// Shares the start time, the keyspace, the group and the options of the hosting tests.
/// </summary>
internal static class HostingTesting
{
    /// <summary>
    /// Gets the time the tests' fake clock starts at.
    /// </summary>
    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Gets the keyspace the tests use.
    /// </summary>
    public static Keyspace Keyspace { get; } = new("hosting");

    /// <summary>
    /// Gets the key of the membership group the tests use.
    /// </summary>
    public static CoordinationKey Group { get; } = Keyspace.CreateKey("members");

    /// <summary>
    /// Creates a fake clock that starts at <see cref="Start" />.
    /// </summary>
    /// <returns>The fake clock.</returns>
    public static FakeTimeProvider CreateTime() => new(Start);

    /// <summary>
    /// Builds the coordination services over the in-process provider and the given clock.
    /// </summary>
    /// <param name="time">The clock the in-process provider expires on.</param>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider BuildCoordination(FakeTimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(time);
        var binding = new ProviderBinding(InProcessCoordinationBuilderExtensions.ProviderName);
        services.AddCoordination(b => b.AddInProcessProvider().Bind(binding));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates the host session service over the given operations, with <see cref="CreateOptions" />.
    /// </summary>
    /// <param name="time">The clock the service runs on.</param>
    /// <param name="sessions">The session operations the service calls.</param>
    /// <param name="membership">The membership operations the service calls.</param>
    /// <returns>The service.</returns>
    public static DefaultHostSessionService CreateService(
        TimeProvider time,
        ISessionService sessions,
        IMembershipService membership
    ) =>
        new(
            sessions,
            membership,
            Options.Create(CreateOptions()),
            time,
            NullLogger<DefaultHostSessionService>.Instance
        );

    /// <summary>
    /// Creates valid options for a node of the tests' cluster.
    /// </summary>
    /// <returns>The options, with the cluster, the node's id and address, and one metadata entry set.</returns>
    public static CoordinationHostOptions CreateOptions()
    {
        var options = new CoordinationHostOptions
        {
            Cluster = "hosting",
            NodeId = "node-1",
            Address = "10.0.0.1:5000",
        };

        options.Metadata["zone"] = "eu-1";
        return options;
    }
}
