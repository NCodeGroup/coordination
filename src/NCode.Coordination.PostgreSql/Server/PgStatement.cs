// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Holds one SQL statement and its positional arguments, which a batch sends as one command.
/// </summary>
/// <param name="sql">The statement's text, whose variable parts are <c>$n</c> parameters.</param>
/// <param name="arguments">The arguments, in the order of their parameters.</param>
internal sealed class PgStatement(string sql, params IEnumerable<PgArgument> arguments)
{
    /// <summary>
    /// Gets the statement's text.
    /// </summary>
    public string Sql { get; } = sql ?? throw new ArgumentNullException(nameof(sql));

    /// <summary>
    /// Gets the arguments, in the order of their parameters.
    /// </summary>
    public ImmutableArray<PgArgument> Arguments { get; } = [.. arguments];

    /// <summary>
    /// Creates the Npgsql batch command that sends the statement.
    /// </summary>
    /// <returns>A new batch command with one parameter per argument.</returns>
    public NpgsqlBatchCommand ToBatchCommand()
    {
        var command = new NpgsqlBatchCommand(Sql);
        foreach (var argument in Arguments)
        {
            command.Parameters.Add(argument.ToParameter());
        }

        return command;
    }
}
