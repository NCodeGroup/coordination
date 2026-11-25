// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class PrimitiveBinderTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    public void Dispose() => Mocks.Verify();

    private static GuaranteeRegistry CreateRegistry() =>
        GuaranteeRegistry.Create(Guarantees.Definitions, []);

    private static ProviderCatalog CreateCatalog(params TestProvider[] providers)
    {
        var factories = providers
            .Select(provider => new Func<IServiceProvider, ICoordinationProvider>(_ => provider))
            .ToList();

        return ProviderCatalog.Create(factories, Services, []);
    }

    private TestProvider CreateProvider(string name, params PrimitiveSupport[] primitives) =>
        new(new ProviderDeclaration(name, primitives), Mocks.Create<IOccurrenceService>().Object);

    #region Bind Tests

    [Fact]
    public void Bind_WhenBindingListsNoPrimitives_BindsEverySupportedOne()
    {
        var failures = new List<string>();
        var custom = new Primitive("custom");
        var provider = CreateProvider(
            "memory",
            PrimitiveSupport.Supported(Primitives.Occurrence),
            PrimitiveSupport.Unsupported(Primitives.Lease, "Not here."),
            PrimitiveSupport.Supported(custom)
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory")],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(failures);
        Assert.Equal([Primitives.Occurrence, custom], bound.Select(x => x.Primitive));
    }

    [Fact]
    public void Bind_WhenBindingListsAPrimitive_BindsOnlyIt()
    {
        var failures = new List<string>();
        var custom = new Primitive("custom");
        var provider = CreateProvider(
            "memory",
            PrimitiveSupport.Supported(Primitives.Occurrence),
            PrimitiveSupport.Supported(custom)
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory", custom)],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(failures);
        var only = Assert.Single(bound);
        Assert.Equal(custom, only.Primitive);
        Assert.Equal("memory", only.Provider);
        Assert.Null(only.Service);
    }

    [Fact]
    public void Bind_KeepsTheBindingsKeyspace()
    {
        var failures = new List<string>();
        var keyspace = new Keyspace("prod", "cell-1");
        var provider = CreateProvider("memory", PrimitiveSupport.Supported(Primitives.Occurrence));

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory") { Keyspace = keyspace }],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(failures);
        var only = Assert.Single(bound);
        Assert.Equal(keyspace, only.Keyspace);
        Assert.NotNull(only.Service);
    }

    [Fact]
    public void Bind_ClosesTheDeclaredGuarantees()
    {
        var failures = new List<string>();
        var provider = CreateProvider(
            "memory",
            PrimitiveSupport.Supported(Primitives.Occurrence, Guarantees.LinearizableUntilFailover)
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory")],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(failures);
        var only = Assert.Single(bound);
        Assert.Contains(Guarantees.LinearizableUntilFailover, only.Guarantees);
        Assert.Contains(Guarantees.LinearizableWithinProcess, only.Guarantees);
    }

    [Fact]
    public void Bind_WhenTheProviderIsNotRegistered_Fails()
    {
        var failures = new List<string>();

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("missing")],
            CreateCatalog(),
            CreateRegistry(),
            failures
        );

        Assert.Empty(bound);
        Assert.Equal(
            ["A binding names the provider 'missing', which is not registered."],
            failures
        );
    }

    [Fact]
    public void Bind_WhenThePrimitiveIsNotListed_Fails()
    {
        var failures = new List<string>();
        var provider = CreateProvider("memory", PrimitiveSupport.Supported(Primitives.Occurrence));

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory", Primitives.Lease)],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(bound);
        Assert.Equal(
            ["The provider 'memory' is bound to the primitive 'lease', which it does not list."],
            failures
        );
    }

    [Fact]
    public void Bind_WhenThePrimitiveIsUnsupported_FailsWithTheReason()
    {
        var failures = new List<string>();
        var provider = CreateProvider(
            "memory",
            PrimitiveSupport.Unsupported(Primitives.Lease, "Leases need a server.")
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory", Primitives.Lease)],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(bound);
        Assert.Equal(
            [
                "The provider 'memory' is bound to the primitive 'lease', which it does not support: Leases need a server.",
            ],
            failures
        );
    }

    [Fact]
    public void Bind_WhenTheProviderServesNoInterface_Fails()
    {
        var failures = new List<string>();
        var provider = new TestProvider(
            new ProviderDeclaration("memory", PrimitiveSupport.Supported(Primitives.Occurrence))
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory")],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(bound);
        Assert.Equal(
            [
                "The provider 'memory' supports the primitive 'occurrence' but serves no IOccurrenceService.",
            ],
            failures
        );
    }

    [Fact]
    public void Bind_WhenThePrimitiveHasNoBuiltInInterface_BindsWithoutAService()
    {
        var failures = new List<string>();
        var custom = new Primitive("custom");
        var provider = new TestProvider(
            new ProviderDeclaration("memory", PrimitiveSupport.Supported(custom))
        );

        var bound = PrimitiveBinder.Bind(
            [new ProviderBinding("memory", custom)],
            CreateCatalog(provider),
            CreateRegistry(),
            failures
        );

        Assert.Empty(failures);
        var only = Assert.Single(bound);
        Assert.Equal(custom, only.Primitive);
        Assert.Null(only.Service);
    }

    #endregion
}
