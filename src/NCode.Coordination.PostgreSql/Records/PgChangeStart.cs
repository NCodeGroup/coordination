// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds what the first batch of a keyspace's change read: the change that the transaction builds, and the rows
/// that the asked keys hold.
/// </summary>
/// <param name="Change">The change, built from the keyspace's revision and the server's instant.</param>
/// <param name="Rows">The rows of the asked keys that exist, expired or not, by key.</param>
internal sealed record PgChangeStart(
    PgKeyspaceChange Change,
    ImmutableDictionary<CoordinationKey, PgRecordRow> Rows
);
