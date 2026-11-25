// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a set that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Add"/>, <see cref="Remove"/>, <see cref="List"/> or <see cref="Clear"/>.</param>
/// <param name="Member">The index of the member the call names, in the members the model was given; unused by <see cref="List"/> and <see cref="Clear"/>.</param>
internal sealed record SetCall(string Operation, int Member)
{
    /// <summary>
    /// The operation that adds a member.
    /// </summary>
    public const string Add = "add";

    /// <summary>
    /// The operation that removes a member.
    /// </summary>
    public const string Remove = "remove";

    /// <summary>
    /// The operation that lists the members.
    /// </summary>
    public const string List = "list";

    /// <summary>
    /// The operation that removes every member.
    /// </summary>
    public const string Clear = "clear";
}
