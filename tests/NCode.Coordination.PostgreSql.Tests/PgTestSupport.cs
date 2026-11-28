// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Conformance;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// Helpers that the PostgreSQL provider's restart and failover tests share.
/// </summary>
internal static class PgTestSupport
{
    /// <summary>
    /// The name of the notification topic that the tests subscribe to.
    /// </summary>
    public const string TopicName = "topic";

    /// <summary>
    /// The name of the record prefix that the tests watch.
    /// </summary>
    public const string WatchedName = "watched";

    /// <summary>
    /// The number of rounds that a test waits for a subscription to resync and deliver again.
    /// </summary>
    public const int Attempts = 20;

    /// <summary>
    /// The prefix of the payloads and record names that a test writes after a fault, numbered by attempt.
    /// </summary>
    public const string AfterPrefix = "after-";

    /// <summary>
    /// Gets how long a test waits for a server to answer again, or for a fault to take a step.
    /// </summary>
    public static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a test waits for the next item of a stream.
    /// </summary>
    private static TimeSpan NextLimit { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Creates a keyspace with a unique id, so that no other test sees its keys.
    /// </summary>
    /// <param name="name">The name of the keyspace, which tells the tests' keys apart.</param>
    /// <returns>The keyspace.</returns>
    public static Keyspace CreateKeyspace(string name)
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace(name, segment);
    }

    /// <summary>
    /// Creates a provider in a schema of its own, so that no other test sees its tables, and probes it.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="profile">The profile of the provider.</param>
    /// <param name="cancellationToken">The token that cancels the creation.</param>
    /// <returns>The probed provider, which the caller disposes.</returns>
    public static ValueTask<PostgreSqlProvider> CreateProviderAsync(
        string connectionString,
        PostgreSqlProfile profile,
        CancellationToken cancellationToken
    )
    {
        var name = $"t_{Guid.NewGuid():N}";
        var schema = PgSchemaName.Create(name);

        return CreateProviderAsync(connectionString, profile, schema, cancellationToken);
    }

    /// <summary>
    /// Creates a provider on a server in a schema that the caller names, so that two providers on two servers that
    /// replicate each other see the same tables, and probes it.
    /// </summary>
    /// <param name="connectionString">The connection string of the server.</param>
    /// <param name="profile">The profile of the provider.</param>
    /// <param name="schema">The schema of the provider's tables.</param>
    /// <param name="cancellationToken">The token that cancels the creation.</param>
    /// <returns>The probed provider, which the caller disposes.</returns>
    public static async ValueTask<PostgreSqlProvider> CreateProviderAsync(
        string connectionString,
        PostgreSqlProfile profile,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        var settings = PgConformance.Settings with { Schema = schema };
        var source = PgSource.FromConnectionString(connectionString);
        var provider = new PostgreSqlProvider(source, profile, settings);

        try
        {
            await provider.ProbeAsync(cancellationToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return provider;
    }

    /// <summary>
    /// Gets a primitive service of the provider, failing the test when the provider offers none.
    /// </summary>
    /// <typeparam name="TService">The service interface to get.</typeparam>
    /// <param name="provider">The provider that offers the service.</param>
    /// <returns>The service.</returns>
    public static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    /// <summary>
    /// Moves an enumerator to its next item, failing the test when none arrives in time.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="enumerator">The enumerator to move.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The next item.</returns>
    public static async ValueTask<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator,
        CancellationToken cancellationToken
    )
    {
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(NextLimit, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    /// <summary>
    /// Repeats a call until it stops failing with a coordination exception, which is how a client sees a server that
    /// has not yet come back or become the primary, and fails the test when a failure isn't unavailable or the server
    /// stays silent for too long.
    /// </summary>
    /// <param name="call">The call to repeat.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the call succeeds.</returns>
    public static async ValueTask WaitUntilAnsweredAsync(
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
    /// Publishes numbered messages until the subscription resyncs and then delivers a later message, and fails the test
    /// when it doesn't within <see cref="Attempts"/> rounds.
    /// </summary>
    /// <param name="notifications">The notification service to publish through.</param>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="enumerator">The enumerator of the subscription to the topic.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the subscription has resynced and delivered.</returns>
    public static async ValueTask AssertTopicResyncsThenDeliversAsync(
        INotificationService notifications,
        CoordinationKey topic,
        IAsyncEnumerator<Notification> enumerator,
        CancellationToken cancellationToken
    )
    {
        var resynced = false;
        var delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var payload = Encoding.UTF8.GetBytes($"{AfterPrefix}{attempt}");
            await WaitUntilAnsweredAsync(
                ct => notifications.PublishAsync(topic, payload, null, ct),
                cancellationToken
            );

            var notification = await NextAsync(enumerator, cancellationToken);
            if (notification.IsResync)
                resynced = true;
            else if (resynced)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);
    }

    /// <summary>
    /// Creates numbered records under <see cref="WatchedName"/> until the watch resyncs and then delivers a later
    /// write, and fails the test when it doesn't within <see cref="Attempts"/> rounds.
    /// </summary>
    /// <param name="records">The record service to write through.</param>
    /// <param name="keyspace">The keyspace that holds the watched records.</param>
    /// <param name="enumerator">The enumerator of the watch on the records.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the watch has resynced and delivered.</returns>
    public static async ValueTask AssertWatchResyncsThenDeliversAsync(
        IRecordService records,
        Keyspace keyspace,
        IAsyncEnumerator<RecordChange> enumerator,
        CancellationToken cancellationToken
    )
    {
        var value = "v"u8.ToArray();
        var resynced = false;
        var delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var key = keyspace.CreateKey(WatchedName, $"{AfterPrefix}{attempt}");
            await WaitUntilAnsweredAsync(
                async ct =>
                {
                    await records.CreateAsync(key, value, RecordLifetime.Persistent, null, ct);
                },
                cancellationToken
            );

            var change = await NextAsync(enumerator, cancellationToken);
            if (change.IsResync)
                resynced = true;
            else if (resynced && change.Kind == RecordChangeKind.Written)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);
    }
}
