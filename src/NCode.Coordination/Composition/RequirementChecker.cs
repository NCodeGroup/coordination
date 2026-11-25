// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// Checks each feature's requirements against the binding that serves its primitive.
/// </summary>
internal static class RequirementChecker
{
    /// <summary>
    /// Checks every requirement, adding a failure for each one a binding cannot keep.
    /// </summary>
    /// <param name="requirements">The requirements, in registration order.</param>
    /// <param name="table">The bindings.</param>
    /// <param name="registry">The defined guarantees.</param>
    /// <param name="singleNode">Whether the host runs as a single node.</param>
    /// <param name="failures">Receives a message for each failure.</param>
    public static void Check(
        IEnumerable<FeatureRequirement> requirements,
        BindingTable table,
        GuaranteeRegistry registry,
        bool singleNode,
        ICollection<string> failures
    )
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(failures);

        foreach (var requirement in requirements)
        {
            var feature = requirement.Feature;
            var primitive = requirement.Primitive;

            var undefined = false;
            foreach (var guarantee in requirement.Guarantees)
            {
                if (registry.IsDefined(guarantee))
                {
                    continue;
                }

                undefined = true;
                failures.Add(
                    $"The feature '{feature}' requires the guarantee '{guarantee.Name}', which is not defined."
                );
            }

            if (undefined)
            {
                continue;
            }

            var scope = BindingTable.DescribeScope(requirement.Keyspace);
            var bound = table.Find(primitive, requirement.Keyspace);
            if (bound is null)
            {
                failures.Add(
                    $"The feature '{feature}' requires the primitive '{primitive}' {scope}, and no provider is bound to it."
                );
                continue;
            }

            var missing = requirement
                .Guarantees.Where(x => !IsKept(bound, x, singleNode))
                .Distinct()
                .Select(x => $"'{x.Name}'")
                .ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var list = string.Join(", ", missing);
            failures.Add(
                $"The feature '{feature}' requires {list} on the primitive '{primitive}' {scope}, which the provider '{bound.Provider}' does not keep."
            );
        }
    }

    private static bool IsKept(BoundPrimitive bound, Guarantee guarantee, bool singleNode)
    {
        if (bound.Guarantees.Contains(guarantee))
        {
            return true;
        }

        return singleNode
            && (
                guarantee == Guarantees.LinearizableAcrossFailover
                || guarantee == Guarantees.LinearizableUntilFailover
            )
            && bound.Guarantees.Contains(Guarantees.LinearizableWithinProcess);
    }
}
