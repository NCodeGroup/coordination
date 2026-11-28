// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Reads the columns that more than one service of the provider reads the same way.
/// </summary>
internal static class PgColumns
{
    /// <summary>
    /// Reads a time stamp column as an instant of kind UTC.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The instant, of kind UTC.</returns>
    public static DateTime ReadUtc(NpgsqlDataReader reader, int ordinal)
    {
        var instant = reader.GetDateTime(ordinal);

        return DateTime.SpecifyKind(instant, DateTimeKind.Utc);
    }

    /// <summary>
    /// Reads a time stamp column that can be null as an instant of kind UTC.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The instant, of kind UTC, or null when the column is null.</returns>
    public static DateTime? ReadNullableUtc(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return ReadUtc(reader, ordinal);
    }
}
