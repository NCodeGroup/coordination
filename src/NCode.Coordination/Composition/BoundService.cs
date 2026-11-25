// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// A provider's implementation of a primitive's interface, with the names a measurement records.
/// </summary>
/// <typeparam name="TService">The primitive's interface.</typeparam>
/// <param name="Provider">The name of the provider.</param>
/// <param name="Primitive">The primitive.</param>
/// <param name="Service">The provider's implementation.</param>
internal readonly record struct BoundService<TService>(
    string Provider,
    Primitive Primitive,
    TService Service
)
    where TService : class;
