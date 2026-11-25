// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// An item taken from an in-process event queue.
/// </summary>
/// <typeparam name="T">The type of the items the queue holds.</typeparam>
/// <param name="Item">The item taken, or the newest item the queue dropped when it overflowed.</param>
/// <param name="Overflowed">
/// A value indicating whether the queue dropped what it held because it was full, in which case <paramref name="Item"/>
/// is the newest item dropped.
/// </param>
internal readonly record struct InProcessQueued<T>(T Item, bool Overflowed);
