// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The base of the contract suites: it creates the provider under test for each test and gives each test a keyspace of
/// its own.
/// </summary>
/// <remarks>
/// A provider's test project derives a sealed class from a suite and overrides <see cref="CreateTargetAsync"/>. The
/// provider is disposed after each test when it implements <see cref="IAsyncDisposable"/> or
/// <see cref="IDisposable"/>.
/// </remarks>
public abstract class ProviderContractTests : IAsyncLifetime
{
    /// <summary>
    /// Gets the duration of <see cref="ShortTimeToLive"/>, 2 seconds.
    /// </summary>
    protected static TimeSpan ShortDuration { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the time to live of the states that a test lets expire, 2 seconds.
    /// </summary>
    protected static TimeToLive ShortTimeToLive { get; } = new(ShortDuration);

    /// <summary>
    /// Gets how far a test advances the clock to show that a short time to live has not ended, 1 second.
    /// </summary>
    protected static TimeSpan StillAlive { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets how far a test advances the clock, after <see cref="StillAlive"/>, to show that a short time to live has
    /// ended, 2 seconds.
    /// </summary>
    protected static TimeSpan PastExpiry { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the duration of <see cref="LongTimeToLive"/>, 30 seconds.
    /// </summary>
    private static TimeSpan LongDuration { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets a time to live that no test outlasts, 30 seconds.
    /// </summary>
    protected static TimeToLive LongTimeToLive { get; } = new(LongDuration);

    /// <summary>
    /// Gets the extensions that no provider knows.
    /// </summary>
    private static ImmutableDictionary<string, string> UnknownExtensions { get; } =
        ImmutableDictionary<string, string>.Empty.Add("conformance.unknown", "on");

    /// <summary>
    /// Gets options with an extension that no provider knows.
    /// </summary>
    protected static OperationOptions UnknownExtension { get; } =
        new() { Extensions = UnknownExtensions };

    /// <summary>
    /// Gets the cancellation token of the running test.
    /// </summary>
    protected static CancellationToken TestCancellationToken =>
        TestContext.Current.CancellationToken;

    /// <summary>
    /// Creates a keyspace that no other test uses.
    /// </summary>
    /// <returns>The keyspace.</returns>
    protected static Keyspace CreateUniqueKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);
        return new Keyspace("conformance", segment);
    }

    /// <summary>
    /// Gets the keyspace of this test, which no other test uses, so tests that share a server never meet.
    /// </summary>
    protected Keyspace Keyspace { get; } = CreateUniqueKeyspace();

    /// <summary>
    /// Gets or sets the target that the running test created, or null before the test starts.
    /// </summary>
    private ConformanceTarget? CurrentTarget { get; set; }

    /// <summary>
    /// Gets the target of the running test.
    /// </summary>
    /// <exception cref="InvalidOperationException">The test has not started.</exception>
    protected ConformanceTarget Target =>
        CurrentTarget
        ?? throw new InvalidOperationException("The target is created when the test starts.");

    /// <summary>
    /// Creates the provider under test, and optionally a way to advance its clock, for one test.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the creation.</param>
    /// <returns>The target of the test.</returns>
    protected abstract ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    );

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        CurrentTarget = await CreateTargetAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var provider = CurrentTarget?.Provider;
        if (provider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // CA1816: a derived suite may declare a finalizer, which disposal suppresses here.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a key in this test's keyspace.
    /// </summary>
    /// <param name="segments">The segments of the key.</param>
    /// <returns>The key.</returns>
    protected CoordinationKey CreateKey(params IEnumerable<KeySegment> segments) =>
        Keyspace.CreateKey(segments);

    /// <summary>
    /// Gets what the provider declares for a primitive, and skips the test when the provider does not support the
    /// primitive or does not list it.
    /// </summary>
    /// <param name="primitive">The primitive the test exercises.</param>
    /// <returns>What the provider declares for the primitive.</returns>
    protected PrimitiveSupport GetSupport(Primitive primitive)
    {
        foreach (var support in Target.Provider.Declaration.Primitives)
        {
            if (!support.Primitive.Equals(primitive))
            {
                continue;
            }

            if (!support.IsSupported)
            {
                Assert.Skip(
                    $"The provider does not support the primitive '{primitive.Name}': {support.UnsupportedReason}"
                );
            }

            return support;
        }

        Assert.Skip($"The provider does not declare the primitive '{primitive.Name}'.");
        throw new UnreachableException();
    }

    /// <summary>
    /// Gets whether the provider declares a guarantee for a primitive, and skips the test when the provider does not
    /// support the primitive or does not list it.
    /// </summary>
    /// <param name="primitive">The primitive the test exercises.</param>
    /// <param name="guarantee">The guarantee to look for.</param>
    /// <returns>True when the provider declares the guarantee for the primitive.</returns>
    protected bool Declares(Primitive primitive, Guarantee guarantee) =>
        GetSupport(primitive).Guarantees.Contains(guarantee);

    /// <summary>
    /// Gets the provider's service for a primitive, and skips the test when the provider does not support the
    /// primitive or does not list it.
    /// </summary>
    /// <typeparam name="TService">The interface of the primitive.</typeparam>
    /// <param name="primitive">The primitive the test exercises.</param>
    /// <returns>The provider's service.</returns>
    /// <remarks>
    /// The test fails when the provider declares the primitive supported but serves no service for it.
    /// </remarks>
    protected TService GetService<TService>(Primitive primitive)
        where TService : class
    {
        _ = GetSupport(primitive);

        var service = Target.Provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);
        return service;
    }

    /// <summary>
    /// Moves the provider's clock forward, and skips the test when the target cannot advance it.
    /// </summary>
    /// <param name="duration">How far to move the clock.</param>
    /// <param name="cancellationToken">A token that cancels the move.</param>
    /// <returns>A task that completes when the clock has moved.</returns>
    protected async ValueTask AdvanceClockAsync(
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        var advance = Target.AdvanceClock;
        if (advance is null)
        {
            Assert.Skip("The target cannot advance its provider's clock.");
            return;
        }

        await advance(duration, cancellationToken);
    }
}
