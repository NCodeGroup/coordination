// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Holds one positional argument of a <see cref="PgStatement"/>, with the PostgreSQL type that Npgsql sends it as
/// when the caller names one.
/// </summary>
/// <param name="Value">The value, or <see cref="DBNull.Value"/> for a typed null.</param>
/// <param name="Type">The PostgreSQL type, or null to let Npgsql infer it from the value.</param>
internal readonly record struct PgArgument(object? Value, NpgsqlDbType? Type)
{
    /// <summary>
    /// Creates an argument whose type Npgsql infers from its value.
    /// </summary>
    /// <param name="value">The value, which is not null.</param>
    /// <returns>The argument.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public static PgArgument Of(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new PgArgument(value, null);
    }

    /// <summary>
    /// Converts a <see cref="long"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    public static implicit operator PgArgument(long value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts an <see cref="int"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    public static implicit operator PgArgument(int value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts a <see cref="short"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    public static implicit operator PgArgument(short value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts a <see cref="string"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value, which is not null.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public static implicit operator PgArgument(string value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts a byte array to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value, which is not null.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public static implicit operator PgArgument(byte[] value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts a <see cref="bool"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    public static implicit operator PgArgument(bool value)
    {
        return Of(value);
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> to an argument whose type Npgsql infers.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The argument <see cref="Of"/> builds.</returns>
    public static implicit operator PgArgument(DateTime value)
    {
        return Of(value);
    }

    /// <summary>
    /// Creates an argument of a named PostgreSQL type, which may be null.
    /// </summary>
    /// <param name="value">The value, or null for a SQL null.</param>
    /// <param name="type">The PostgreSQL type.</param>
    /// <returns>The argument; a null value becomes <see cref="DBNull.Value"/>.</returns>
    public static PgArgument Typed(object? value, NpgsqlDbType type)
    {
        return new PgArgument(value ?? DBNull.Value, type);
    }

    /// <summary>
    /// Creates the Npgsql parameter that carries the argument.
    /// </summary>
    /// <returns>A new positional parameter.</returns>
    public NpgsqlParameter ToParameter()
    {
        var parameter = new NpgsqlParameter { Value = Value ?? DBNull.Value };
        if (Type is { } type)
        {
            parameter.NpgsqlDbType = type;
        }

        return parameter;
    }
}
