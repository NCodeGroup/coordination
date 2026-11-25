// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a membership group that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Join"/>, <see cref="Leave"/> or <see cref="Read"/>.</param>
/// <param name="Client">The index of the client whose member the call names, in the sessions the model was given; unused by <see cref="Read"/>.</param>
internal sealed record MembershipCall(string Operation, int Client)
{
    /// <summary>
    /// The operation that joins a member.
    /// </summary>
    public const string Join = "join";

    /// <summary>
    /// The operation that removes a member.
    /// </summary>
    public const string Leave = "leave";

    /// <summary>
    /// The operation that reads the members.
    /// </summary>
    public const string Read = "read";
}
