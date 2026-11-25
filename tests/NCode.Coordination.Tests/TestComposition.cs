// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;

namespace NCode.Coordination.Tests;

/// <summary>
/// Builds a composition around one test provider.
/// </summary>
internal static class TestComposition
{
    /// <summary>
    /// The name of the test provider.
    /// </summary>
    public const string ProviderName = "test";

    /// <summary>
    /// Gets the keyspace the tests use.
    /// </summary>
    public static Keyspace Keyspace { get; } = new("test");

    /// <summary>
    /// Gets a key in the tests' keyspace.
    /// </summary>
    public static CoordinationKey Key { get; } = Keyspace.CreateKey("key");

    /// <summary>
    /// Builds services whose only provider serves a primitive with the given implementation, bound to every keyspace.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="primitive">The primitive.</param>
    /// <param name="service">The implementation the provider serves.</param>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider Build<TService>(Primitive primitive, TService service)
        where TService : class
    {
        var binding = new ProviderBinding(ProviderName);
        return BuildBound(primitive, service, binding);
    }

    /// <summary>
    /// Builds services whose only provider serves a primitive with the given implementation, bound to the keyspace
    /// <c>elsewhere</c> only, so no binding covers <see cref="Keyspace" />.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="primitive">The primitive.</param>
    /// <param name="service">The implementation the provider serves.</param>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider BuildElsewhere<TService>(Primitive primitive, TService service)
        where TService : class
    {
        var elsewhere = new Keyspace("elsewhere");
        var binding = new ProviderBinding(ProviderName) { Keyspace = elsewhere };
        return BuildBound(primitive, service, binding);
    }

    /// <summary>
    /// Builds services whose only provider serves a primitive with the given implementation, under the given binding.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="primitive">The primitive.</param>
    /// <param name="service">The implementation the provider serves.</param>
    /// <param name="binding">The binding of the provider.</param>
    /// <returns>The built service provider.</returns>
    private static ServiceProvider BuildBound<TService>(
        Primitive primitive,
        TService service,
        ProviderBinding binding
    )
        where TService : class
    {
        var support = PrimitiveSupport.Supported(primitive);
        var declaration = new ProviderDeclaration(ProviderName, support);
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, service)).Bind(binding)
        );
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Streams items, yielding to the scheduler before each.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The stream.</returns>
    public static async IAsyncEnumerable<T> Stream<T>(params IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
