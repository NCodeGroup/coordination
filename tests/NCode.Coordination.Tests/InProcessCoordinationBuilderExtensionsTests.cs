// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;

namespace NCode.Coordination.Tests;

public sealed class InProcessCoordinationBuilderExtensionsTests
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        return services;
    }

    private static void AddInProcess(ServiceCollection services)
    {
        var binding = new ProviderBinding(InProcessCoordinationBuilderExtensions.ProviderName);
        services.AddCoordination(b => b.AddInProcessProvider().Bind(binding));
    }

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
        Assert.NotNull(read);
        var actual = read.Value.ToArray();

        Assert.True(created.IsWritten);
        Assert.Equal(value, actual);
        Assert.Equal(created.Revision, read.Revision);
    }

    [Fact]
    public async Task AddInProcessProvider_WhenATimeProviderIsRegistered_ExpiresOnIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var services = CreateServices();
        services.AddSingleton<TimeProvider>(time);
        AddInProcess(services);
        await using var serviceProvider = services.BuildServiceProvider();
        var records = serviceProvider.GetRequiredService<IRecordService>();
        var key = TestComposition.Key;
        var ttl = TimeSpan.FromSeconds(2);
        var timeToLive = new TimeToLive(ttl);
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);
        byte[] value = [1];
        var created = await records.CreateAsync(key, value, lifetime, null, cancellationToken);
        Assert.True(created.IsWritten);
        var before = await records.ReadAsync(key, null, cancellationToken);
        Assert.NotNull(before);

        var elapsed = TimeSpan.FromSeconds(3);
        time.Advance(elapsed);

        var after = await records.ReadAsync(key, null, cancellationToken);

        Assert.Null(after);
    }

    [Fact]
    public async Task AddInProcessProvider_WhenBound_ServesLeases()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = CreateServices();
        AddInProcess(services);
        await using var serviceProvider = services.BuildServiceProvider();
        var leases = serviceProvider.GetRequiredService<ILeaseService>();
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        var result = await leases.TryAcquireAsync(
            TestComposition.Key,
            terms,
            null,
            cancellationToken
        );

        Assert.True(result.IsAcquired);
    }

    [Fact]
    public async Task AddInProcessProvider_WhenBound_ServesSessions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = CreateServices();
        AddInProcess(services);
        await using var serviceProvider = services.BuildServiceProvider();
        var sessions = serviceProvider.GetRequiredService<ISessionService>();
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));
        var grant = await sessions.OpenAsync(
            TestComposition.Keyspace,
            timeToLive,
            null,
            cancellationToken
        );

        var result = await sessions.KeepAliveAsync(grant.Id, null, cancellationToken);

        Assert.True(result.IsRenewed);
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
