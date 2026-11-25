// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class ProviderCatalogTests
{
    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    private static TestProvider CreateProvider(string name, params PrimitiveSupport[] primitives) =>
        new(new ProviderDeclaration(name, primitives));

    #region Create Tests

    [Fact]
    public void Create_WhenProvidersAreValid_KeepsThemInOrder()
    {
        var failures = new List<string>();
        var first = CreateProvider("first");
        var second = CreateProvider("second");

        var catalog = ProviderCatalog.Create([_ => first, _ => second], Services, failures);

        Assert.Empty(failures);
        Assert.Equal<ICoordinationProvider>([first, second], catalog.Providers);
    }

    [Fact]
    public void Create_WhenAFactoryReturnsNull_Fails()
    {
        var failures = new List<string>();
        var first = CreateProvider("first");

        var catalog = ProviderCatalog.Create([_ => null!, _ => first], Services, failures);

        Assert.Equal(["A provider factory returned no provider."], failures);
        Assert.Equal<ICoordinationProvider>([first], catalog.Providers);
    }

    [Fact]
    public void Create_WhenTwoProvidersShareAName_FailsAndKeepsTheFirst()
    {
        var failures = new List<string>();
        var first = CreateProvider("memory");
        var second = CreateProvider("memory");

        var catalog = ProviderCatalog.Create([_ => first, _ => second], Services, failures);

        Assert.Equal(["Two providers are named 'memory'."], failures);
        Assert.Equal<ICoordinationProvider>([first], catalog.Providers);
        Assert.Same(first, catalog.Find("memory"));
    }

    [Fact]
    public void Create_WhenADeclarationListsAPrimitiveTwice_Fails()
    {
        var failures = new List<string>();
        var provider = CreateProvider(
            "memory",
            PrimitiveSupport.Supported(Primitives.Lease),
            PrimitiveSupport.Unsupported(Primitives.Lease, "Not twice.")
        );

        var catalog = ProviderCatalog.Create([_ => provider], Services, failures);

        Assert.Equal(
            ["The provider 'memory' lists the primitive 'lease' more than once."],
            failures
        );
        Assert.Equal<ICoordinationProvider>([provider], catalog.Providers);
    }

    [Fact]
    public void Create_PassesTheServicesToEachFactory()
    {
        var failures = new List<string>();
        var received = new List<IServiceProvider>();
        var provider = CreateProvider("memory");

        ProviderCatalog.Create(
            [
                services =>
                {
                    received.Add(services);
                    return provider;
                },
            ],
            Services,
            failures
        );

        Assert.Same(Services, Assert.Single(received));
    }

    #endregion

    #region Find Tests

    [Fact]
    public void Find_WhenNameIsRegistered_ReturnsTheProvider()
    {
        var failures = new List<string>();
        var provider = CreateProvider("memory");
        var catalog = ProviderCatalog.Create([_ => provider], Services, failures);

        var found = catalog.Find("memory");

        Assert.Same(provider, found);
    }

    [Fact]
    public void Find_WhenNameIsUnknown_ReturnsNull()
    {
        var failures = new List<string>();
        var catalog = ProviderCatalog.Create([_ => CreateProvider("memory")], Services, failures);

        var found = catalog.Find("other");

        Assert.Null(found);
    }

    #endregion
}
