// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds what a list of the records under a prefix needs besides the keyspace's id.
/// </summary>
/// <param name="Prefix">The prefix whose records to list.</param>
/// <param name="Limit">The most records the page holds.</param>
/// <param name="PrefixIndex">The prefix's index value.</param>
/// <param name="PrefixEnd">The first index value after those that start with the prefix, or null when there is none.</param>
/// <param name="StartIndex">The index value after which the page starts.</param>
/// <param name="StartDepth">The depth after which the page starts, among the rows of the start index value.</param>
internal sealed record PgListRequest(
    CoordinationKey Prefix,
    int Limit,
    byte[] PrefixIndex,
    byte[]? PrefixEnd,
    byte[] StartIndex,
    short StartDepth
);
