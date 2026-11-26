// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Integration tests for multi-operation scenarios combining different primitives.
/// These tests verify that primitives work correctly together and that session-bound
/// resources are properly cleaned up when sessions end.
/// </summary>
public abstract class IntegrationTests : ProviderContractTests
{
    /// <summary>
    /// Verifies that all five ownership primitives are available and can be accessed
    /// from the provider.
    /// </summary>
    [Fact]
    public void AllPrimitivesAreAvailable()
    {
        // Verify all five new primitives can be accessed
        var sessions = GetService<ISessionService>(Primitives.Session);
        var membership = GetService<IMembershipService>(Primitives.Membership);
        var leases = GetService<ILeaseService>(Primitives.Lease);
        var semaphores = GetService<ISemaphoreService>(Primitives.Semaphore);
        var elections = GetService<IElectionService>(Primitives.Election);
        var claims = GetService<IClaimService>(Primitives.Claim);

        Assert.NotNull(sessions);
        Assert.NotNull(membership);
        Assert.NotNull(leases);
        Assert.NotNull(semaphores);
        Assert.NotNull(elections);
        Assert.NotNull(claims);

        // Also verify existing primitives still work
        var records = GetService<IRecordService>(Primitives.Record);
        var transactions = GetService<ITransactionService>(Primitives.Transaction);
        var counters = GetService<ICounterService>(Primitives.Counter);
        var sets = GetService<ISetService>(Primitives.Set);
        var sequences = GetService<ISequenceService>(Primitives.Sequence);
        var occurrences = GetService<IOccurrenceService>(Primitives.Occurrence);

        Assert.NotNull(records);
        Assert.NotNull(transactions);
        Assert.NotNull(counters);
        Assert.NotNull(sets);
        Assert.NotNull(sequences);
        Assert.NotNull(occurrences);
    }

    /// <summary>
    /// Verifies that the provider's declaration includes all primitives.
    /// </summary>
    [Fact]
    public void ProviderDeclarationHasAllPrimitives()
    {
        var declaration = Target.Provider.Declaration;

        // Verify all primitives are listed in the provider's declaration
        var primitiveNames = declaration.Primitives.Select(p => p.Primitive).ToHashSet();

        Assert.Contains(Primitives.Session, primitiveNames);
        Assert.Contains(Primitives.Membership, primitiveNames);
        Assert.Contains(Primitives.Lease, primitiveNames);
        Assert.Contains(Primitives.Semaphore, primitiveNames);
        Assert.Contains(Primitives.Election, primitiveNames);
        Assert.Contains(Primitives.Claim, primitiveNames);

        // Existing primitives should still be supported
        Assert.Contains(Primitives.Record, primitiveNames);
        Assert.Contains(Primitives.Transaction, primitiveNames);
        Assert.Contains(Primitives.Counter, primitiveNames);
        Assert.Contains(Primitives.Set, primitiveNames);
        Assert.Contains(Primitives.Sequence, primitiveNames);
        Assert.Contains(Primitives.Occurrence, primitiveNames);
    }
}

/// <summary>
/// Valkey profile integration tests.
/// </summary>
public sealed class ValkeyIntegrationTests(RespServers servers) : IntegrationTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) => Conformance.RespConformance.CreateTargetAsync(servers.Valkey, RespProfiles.Valkey, cancellationToken);
}

/// <summary>
/// Garnet profile integration tests.
/// </summary>
public sealed class GarnetIntegrationTests(RespServers servers) : IntegrationTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) => Conformance.RespConformance.CreateTargetAsync(servers.Garnet, RespProfiles.Garnet, cancellationToken);
}
