// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// A pass of the sweep: one statement that deletes rows, named for its log entries.
/// </summary>
/// <param name="Name">The name that a log entry gives the pass.</param>
/// <param name="Sql">The statement, whose only argument is the batch size and which returns the count of rows it deleted.</param>
internal sealed record PgSweepPass(string Name, string Sql);
