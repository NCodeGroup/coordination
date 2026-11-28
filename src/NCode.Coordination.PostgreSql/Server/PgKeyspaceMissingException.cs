// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Signals that a statement found its keyspace's row missing, so nothing applied and the keyspace's id must be
/// resolved again.
/// </summary>
internal sealed class PgKeyspaceMissingException : Exception
{
    /// <summary>
    /// The message of the exception that a statement throws when its keyspace's row does not exist.
    /// </summary>
    public const string RowMissingMessage = "The keyspace's row does not exist.";

    /// <summary>
    /// Throws when a read of a keyspace's row found none.
    /// </summary>
    /// <param name="rowCount">The number of rows the read found.</param>
    /// <exception cref="PgKeyspaceMissingException">The read found no row.</exception>
    public static void ThrowIfNoRow(int rowCount)
    {
        if (rowCount == 0)
        {
            throw new PgKeyspaceMissingException(RowMissingMessage);
        }
    }

    /// <summary>
    /// Creates the exception without a message.
    /// </summary>
    public PgKeyspaceMissingException() { }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public PgKeyspaceMissingException(string? message)
        : base(message) { }

    /// <summary>
    /// Creates the exception with a message and the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public PgKeyspaceMissingException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
