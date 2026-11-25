// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.Tests;

public sealed class InProcessCoordinationBuilderExtensionsTests
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        return services;
    }

    private static void AddInProcess(ServiceCollection services) =>
        services.AddCoordination(b =>
            b.AddInProcessProvider()
                .Bind(new ProviderBinding(InProcessCoordinationBuilderExtensions.ProviderName))
        );

    #region AddInProcessProvider Tests

    [Fact]
    public async Task AddInProcessProvider_WhenBound_ServesRecords()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = CreateServices();
        AddInProcess(services);
        await using var serviceProvider = services.BuildServiceProvider();
        var records = serviceProvider.GetRequiredService<IRecordService>();
        var key = TestComposition.Key;
        byte[] value = [1, 2, 3];

        var created = await records.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        var read = await records.ReadAsync(key, null, cancellationToken);

        Assert.True(created.IsWritten);
        Assert.NotNull(read);
        Assert.Equal(value, read.Value.ToArray());
        Assert.Equal(created.Revision, read.Revision);
    }

    [Fact]
    public async Task AddInProcessProvider_WhenATimeProviderIsRegistered_ExpiresOnIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(Start);
        var services = CreateServices();
        services.AddSingleton<TimeProvider>(time);
        AddInProcess(services);
        await using var serviceProvider = services.BuildServiceProvider();
        var records = serviceProvider.GetRequiredService<IRecordService>();
        var key = TestComposition.Key;
        var lifetime = RecordLifetime.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(2)));
        var created = await records.CreateAsync(
            key,
            new byte[] { 1 },
            lifetime,
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);
        Assert.NotNull(await records.ReadAsync(key, null, cancellationToken));

        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Null(await records.ReadAsync(key, null, cancellationToken));
    }

    [Fact]
    public void AddInProcessProvider_WhenBuilderIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            InProcessCoordinationBuilderExtensions.AddInProcessProvider(null!)
        );

        Assert.Equal("builder", exception.ParamName);
    }

    #endregion
}
