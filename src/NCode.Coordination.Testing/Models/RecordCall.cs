// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a record that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Create"/>, <see cref="Read"/>, <see cref="CompareAndSwap"/>, <see cref="CompareAndDelete"/>, <see cref="Take"/> or <see cref="List"/>.</param>
/// <param name="Value">The value the call writes, or null for an operation that writes none.</param>
/// <param name="Expected">The revision value the call expects, or 0 for an operation that expects none.</param>
internal sealed record RecordCall(string Operation, string? Value, long Expected)
{
    /// <summary>
    /// The operation that creates the record.
    /// </summary>
    public const string Create = "create";

    /// <summary>
    /// The operation that reads the record.
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// The operation that replaces the record when its revision is the expected one.
    /// </summary>
    public const string CompareAndSwap = "compare-and-swap";

    /// <summary>
    /// The operation that deletes the record when its revision is the expected one.
    /// </summary>
    public const string CompareAndDelete = "compare-and-delete";

    /// <summary>
    /// The operation that reads the record and deletes it.
    /// </summary>
    public const string Take = "take";

    /// <summary>
    /// The operation that lists the records under a prefix, which spans keys.
    /// </summary>
    public const string List = "list";
}
