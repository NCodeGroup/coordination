// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// The guarantees an application can name, each with its meaning, and what each implies.
/// </summary>
internal sealed class GuaranteeRegistry
{
    /// <summary>
    /// Creates a registry from definitions, returning a failure for each conflict or undefined implication.
    /// </summary>
    /// <param name="definitions">The definitions, in order.</param>
    /// <returns>The registry of the accepted definitions and the failures found.</returns>
    public static Checked<GuaranteeRegistry> Create(IEnumerable<GuaranteeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var failures = new List<string>();
        var accepted = new Dictionary<Guarantee, GuaranteeDefinition>();
        foreach (var definition in definitions)
        {
            if (!accepted.TryGetValue(definition.Guarantee, out var existing))
            {
                accepted.Add(definition.Guarantee, definition);
            }
            else if (!SameMeaning(existing, definition))
            {
                failures.Add(
                    $"The guarantee '{definition.Guarantee.Name}' is defined twice, with different meanings."
                );
            }
        }

        foreach (var definition in accepted.Values)
        {
            foreach (var implied in definition.Implies)
            {
                if (!accepted.ContainsKey(implied))
                {
                    failures.Add(
                        $"The guarantee '{definition.Guarantee.Name}' implies '{implied.Name}', which is not defined."
                    );
                }
            }
        }

        var frozen = accepted.ToFrozenDictionary();
        var registry = new GuaranteeRegistry(frozen);
        return new Checked<GuaranteeRegistry>(registry, [.. failures]);
    }

    /// <summary>
    /// Gets whether two definitions of one guarantee mean the same.
    /// </summary>
    /// <param name="first">The first definition.</param>
    /// <param name="second">The second definition.</param>
    /// <returns>True when the dimension, the description and the implied guarantees are equal.</returns>
    private static bool SameMeaning(GuaranteeDefinition first, GuaranteeDefinition second) =>
        string.Equals(first.Dimension, second.Dimension, StringComparison.Ordinal)
        && string.Equals(first.Description, second.Description, StringComparison.Ordinal)
        && first.Implies.SequenceEqual(second.Implies);

    /// <summary>
    /// Creates a registry over the accepted definitions.
    /// </summary>
    /// <param name="definitions">The accepted definitions, by guarantee.</param>
    private GuaranteeRegistry(FrozenDictionary<Guarantee, GuaranteeDefinition> definitions)
    {
        Definitions = definitions;
    }

    /// <summary>
    /// Gets the accepted definition of each defined guarantee.
    /// </summary>
    private FrozenDictionary<Guarantee, GuaranteeDefinition> Definitions { get; }

    /// <summary>
    /// Gets whether the registry defines a guarantee.
    /// </summary>
    /// <param name="guarantee">The guarantee to look up.</param>
    /// <returns>True when the guarantee is defined.</returns>
    public bool IsDefined(Guarantee guarantee) => Definitions.ContainsKey(guarantee);

    /// <summary>
    /// Gets the guarantees and everything they imply, transitively.
    /// </summary>
    /// <param name="guarantees">The guarantees to close.</param>
    /// <returns>The guarantees and the ones they imply.</returns>
    public ImmutableHashSet<Guarantee> Close(IEnumerable<Guarantee> guarantees)
    {
        ArgumentNullException.ThrowIfNull(guarantees);

        var seen = new HashSet<Guarantee>();
        var work = new Stack<Guarantee>(guarantees);
        while (work.TryPop(out var current))
        {
            if (!seen.Add(current))
            {
                continue;
            }

            if (Definitions.TryGetValue(current, out var definition))
            {
                foreach (var implied in definition.Implies)
                {
                    work.Push(implied);
                }
            }
        }

        return [.. seen];
    }
}
