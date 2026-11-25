// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition.Tests;

public sealed class RequirementCheckerTests
{
    private static Keyspace Tenant { get; } = new("prod", "cell-1");

    private static GuaranteeRegistry Registry { get; } =
        GuaranteeRegistry.Create(Guarantees.Definitions, []);

    private static BoundPrimitive Bound(
        string provider,
        Primitive primitive,
        Keyspace? keyspace,
        params Guarantee[] guarantees
    )
    {
        var closed = Registry.Close(guarantees);
        return new BoundPrimitive(provider, primitive, keyspace, null, closed);
    }

    private static BindingTable Build(params BoundPrimitive[] bound) =>
        BindingTable.Create(bound, []);

    private static List<string> Check(
        BindingTable table,
        bool singleNode,
        params FeatureRequirement[] requirements
    )
    {
        var failures = new List<string>();
        RequirementChecker.Check(requirements, table, Registry, singleNode, failures);
        return failures;
    }

    #region Check Tests

    [Fact]
    public void Check_WhenTheBindingKeepsEveryGuarantee_DoesNotFail()
    {
        var entry = Bound(
            "memory",
            Primitives.Occurrence,
            null,
            Guarantees.Durable,
            Guarantees.ServerClock
        );
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.Durable,
            Guarantees.ServerClock
        );

        var failures = Check(table, false, requirement);

        Assert.Empty(failures);
    }

    [Fact]
    public void Check_WhenAnImpliedGuaranteeIsRequired_DoesNotFail()
    {
        var entry = Bound(
            "memory",
            Primitives.Occurrence,
            null,
            Guarantees.LinearizableAcrossFailover
        );
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.LinearizableUntilFailover,
            Guarantees.LinearizableWithinProcess
        );

        var failures = Check(table, false, requirement);

        Assert.Empty(failures);
    }

    [Fact]
    public void Check_WhenGuaranteesAreMissing_ListsEachWithFeaturePrimitiveAndProvider()
    {
        var entry = Bound("memory", Primitives.Occurrence, null, Guarantees.Volatile);
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.LinearizableAcrossFailover,
            Guarantees.Volatile,
            Guarantees.Durable,
            Guarantees.Durable
        );

        var failures = Check(table, false, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires 'consistency.linearizable-across-failover', 'durability.durable' on the primitive 'occurrence' by default, which the provider 'memory' does not keep.",
            failure
        );
    }

    [Fact]
    public void Check_WhenNoBindingServesThePrimitive_Fails()
    {
        var entry = Bound("memory", Primitives.Lease, null);
        var table = Build(entry);
        var requirement = new FeatureRequirement("replay", Primitives.Occurrence);

        var failures = Check(table, false, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires the primitive 'occurrence' by default, and no provider is bound to it.",
            failure
        );
    }

    [Fact]
    public void Check_WhenTheRequirementNamesAKeyspace_UsesItsBinding()
    {
        var fast = Bound("fast", Primitives.Occurrence, null, Guarantees.Durable);
        var slow = Bound("slow", Primitives.Occurrence, Tenant);
        var table = Build(fast, slow);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.Durable
        )
        {
            Keyspace = Tenant,
        };

        var failures = Check(table, false, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires 'durability.durable' on the primitive 'occurrence' for the keyspace 'prod/cell-1', which the provider 'slow' does not keep.",
            failure
        );
    }

    [Fact]
    public void Check_WhenAGuaranteeIsUndefined_Fails()
    {
        var entry = Bound("memory", Primitives.Occurrence, null);
        var table = Build(entry);
        var unknown = new Guarantee("custom.unknown");
        var requirement = new FeatureRequirement("replay", Primitives.Occurrence, unknown);

        var failures = Check(table, false, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires the guarantee 'custom.unknown', which is not defined.",
            failure
        );
    }

    [Fact]
    public void Check_WhenSingleNode_AcceptsWithinProcessForLinearizability()
    {
        var entry = Bound(
            "memory",
            Primitives.Occurrence,
            null,
            Guarantees.LinearizableWithinProcess
        );
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.LinearizableUntilFailover,
            Guarantees.LinearizableAcrossFailover
        );

        var failures = Check(table, true, requirement);

        Assert.Empty(failures);
    }

    [Fact]
    public void Check_WhenNotSingleNode_RefusesWithinProcessForLinearizability()
    {
        var entry = Bound(
            "memory",
            Primitives.Occurrence,
            null,
            Guarantees.LinearizableWithinProcess
        );
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.LinearizableUntilFailover
        );

        var failures = Check(table, false, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires 'consistency.linearizable-until-failover' on the primitive 'occurrence' by default, which the provider 'memory' does not keep.",
            failure
        );
    }

    [Fact]
    public void Check_WhenSingleNode_StillRefusesOtherMissingGuarantees()
    {
        var entry = Bound(
            "memory",
            Primitives.Occurrence,
            null,
            Guarantees.LinearizableWithinProcess
        );
        var table = Build(entry);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.LinearizableAcrossFailover,
            Guarantees.Durable
        );

        var failures = Check(table, true, requirement);

        var failure = Assert.Single(failures);
        Assert.Equal(
            "The feature 'replay' requires 'durability.durable' on the primitive 'occurrence' by default, which the provider 'memory' does not keep.",
            failure
        );
    }

    [Fact]
    public void Check_WhenSeveralRequirementsFail_ListsEveryOne()
    {
        var entry = Bound("memory", Primitives.Occurrence, null);
        var table = Build(entry);
        var first = new FeatureRequirement("replay", Primitives.Occurrence, Guarantees.Durable);
        var second = new FeatureRequirement("locks", Primitives.Lease);

        var failures = Check(table, false, first, second);

        Assert.Equal(2, failures.Count);
        Assert.StartsWith("The feature 'replay' requires 'durability.durable'", failures[0]);
        Assert.StartsWith("The feature 'locks' requires the primitive 'lease'", failures[1]);
    }

    #endregion
}
