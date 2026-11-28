// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds a group's key with the values that its rows are found by.
/// </summary>
/// <param name="Group">The group's key.</param>
/// <param name="Index">The group's index value.</param>
/// <param name="Depth">The group's partition depth.</param>
/// <param name="IndexArgument">The group's index value as a statement's argument.</param>
internal sealed record PgGroupAddress(
    CoordinationKey Group,
    byte[] Index,
    short Depth,
    PgArgument IndexArgument
);
