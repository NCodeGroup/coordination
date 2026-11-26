// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace NCode.Coordination.Resp.Tests;

public sealed class RespCoordinationBuilderExtensionsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    [Fact]
    public void AddRespProvider_NeitherSet_Throws()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddRespProvider(_ => { })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.StartsWith(
            "Set exactly one of RespProviderOptions.Configuration and RespProviderOptions.Connection.",
            exception.Message
        );
    }

    [Fact]
    public void AddRespProvider_BothSet_Throws()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddRespProvider(options =>
            {
                options.Configuration = "localhost:1";
                options.Connection = _ => throw new InvalidOperationException();
            })
        );

        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public async Task AddRespProvider_Configuration_AddsProviderWithProfile()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        builder
            .Setup(x => x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>()))
            .Callback<Func<IServiceProvider, ICoordinationProvider>>(f => factory = f)
            .Returns(builder.Object)
            .Verifiable();
        await using var services = new ServiceCollection().BuildServiceProvider();

        var result = builder.Object.AddRespProvider(options =>
        {
            options.Configuration = "localhost:1";
            options.Profile = RespProfiles.Garnet;
        });

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
        var provider = factory(services);
        await using var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        Assert.Equal("garnet", provider.Declaration.Profile);
    }
}
