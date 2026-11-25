// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Validation;

/// <summary>
/// The one rule for a list a type keeps: it is an array of its own, and no element is null.
/// </summary>
internal static class ElementList
{
    /// <summary>
    /// Copies a sequence into an immutable array and checks that no element is null.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="items">The elements to copy.</param>
    /// <param name="paramName">The name of the parameter that holds the elements.</param>
    /// <returns>An array with the elements, in their order.</returns>
    public static ImmutableArray<T> Copy<T>(IEnumerable<T> items, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);

        if (items is ImmutableArray<T> array)
        {
            return Check(array, paramName);
        }

        var copy = items.ToImmutableArray();
        ThrowIfNullElement(copy, paramName);

        return copy;
    }

    /// <summary>
    /// Checks that an immutable array holds an array and that no element is null.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="items">The array to check.</param>
    /// <param name="paramName">The name of the parameter or property that holds the array.</param>
    /// <returns>The array, when it is valid.</returns>
    public static ImmutableArray<T> Check<T>(ImmutableArray<T> items, string paramName)
        where T : class
    {
        if (items.IsDefault)
        {
            throw new ArgumentException(
                "The list is a default ImmutableArray, which holds no array.",
                paramName
            );
        }

        ThrowIfNullElement(items, paramName);

        return items;
    }

    /// <summary>Throws when the array holds a null element, naming the parameter that holds it.</summary>
    private static void ThrowIfNullElement<T>(ImmutableArray<T> items, string paramName)
        where T : class
    {
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("The list holds a null element.", paramName);
            }
        }
    }
}
