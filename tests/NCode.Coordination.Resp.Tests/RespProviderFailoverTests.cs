// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Proves what a RESP provider keeps and loses when a replica is promoted while its primary still takes writes, each
/// test failing over servers of its own.
/// </summary>
public sealed class RespProviderFailoverTests
{
    /// <summary>
    /// The client option that has StackExchange.Redis check every server's role each second, so it finds a promoted
    /// Valkey replica with no Sentinel to announce it.
    /// </summary>
    private const string RoleCheckOption = ",configCheckSeconds=1";

    /// <summary>
    /// The name of the record, and the payload of the message, that the old primary takes after the promotion.
    /// </summary>
    private const string LostName = "lost";

    /// <summary>
    /// Gets how long a test waits for the client to find the new primary.
    /// </summary>
    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a keyspace with a unique id, so that no other test sees its keys.
    /// </summary>
    /// <returns>The keyspace.</returns>
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("failover", segment);
    }

    /// <summary>
    /// Gets a primitive service of the provider, failing the test when the provider offers none.
    /// </summary>
    /// <typeparam name="TService">The service interface to get.</typeparam>
    /// <param name="provider">The provider that offers the service.</param>
    /// <returns>The service.</returns>
    private static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    /// <summary>
    /// Repeats a call until it stops failing with a coordination exception, which is how a client sees a server that
    /// has not yet become the primary, and fails the test when the server stays silent for too long. Every failure
    /// while the client finds the new primary must be unavailable.
    /// </summary>
    /// <param name="call">The call to repeat.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the call succeeds.</returns>
    private static async ValueTask WaitUntilAnsweredAsync(
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var pause = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try
            {
                await call(cancellationToken);
                return;
            }
            catch (CoordinationException exception)
            {
                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                if (stopwatch.Elapsed > WaitLimit)
                    Assert.Fail(
                        $"The server did not answer within {WaitLimit}: {exception.Message}"
                    );

                await Task.Delay(pause, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Promotes a replica while the provider writes and checks that the write the old primary took after the promotion
    /// is the only one lost.
    /// </summary>
    /// <param name="start">The function that starts the primary and its replica.</param>
    /// <param name="profile">The profile of the provider that writes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertWritesAcrossAFailoverAsync(
        Func<CancellationToken, ValueTask<IRespFailoverServer>> start,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await start(cancellationToken);
        var endpoint = server.Endpoint + RoleCheckOption;
        var target = await RespConformance.CreateTargetAsync(endpoint, profile, cancellationToken);
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var records = GetService<IRecordService>(target.Provider);
        var keyspace = CreateKeyspace();
        var value = "v"u8.ToArray();
        var beforeKey = keyspace.CreateKey("before");
        var lostKey = keyspace.CreateKey(LostName);
        var afterKey = keyspace.CreateKey("after");

        var before = await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(before.IsWritten);
        await server.WaitForReplicaAsync(cancellationToken);
        await server.PromoteReplicaAsync(cancellationToken);
        var lost = await records.CreateAsync(
            lostKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(lost.IsWritten);
        await server.DemotePrimaryAsync(cancellationToken);
        await WaitUntilAnsweredAsync(
            async ct =>
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct),
            cancellationToken
        );

        var beforeRead = await records.ReadAsync(beforeKey, null, cancellationToken);
        var lostRead = await records.ReadAsync(lostKey, null, cancellationToken);
        var afterRead = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(beforeRead);
        Assert.Null(lostRead);
        Assert.NotNull(afterRead);
    }

    #region CreateAsync Tests

    [RespServerFact]
    public async Task CreateAsync_WhenValkeyFailsOverWhileThePrimaryTakesWrites_LosesOnlyTheWritesAfterThePromotion() =>
        await AssertWritesAcrossAFailoverAsync(
            RespValkeyFailoverServer.StartAsync,
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task CreateAsync_WhenGarnetFailsOverWhileThePrimaryTakesWrites_LosesOnlyTheWritesAfterThePromotion() =>
        await AssertWritesAcrossAFailoverAsync(
            RespGarnetFailoverServer.StartAsync,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion
}
