// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// An item that a subscriber read, stamped on the history's clock.
/// </summary>
/// <typeparam name="T">The type of the item.</typeparam>
/// <param name="Stamp">The stamp on the history's clock at which the subscriber read the item.</param>
/// <param name="Item">The item that the subscriber read.</param>
internal sealed record Delivery<T>(long Stamp, T Item);
