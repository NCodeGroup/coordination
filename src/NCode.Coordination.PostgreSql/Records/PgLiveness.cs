// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Decides whether a stored record still holds, from its own deadline and the row of the session it is bound to.
/// </summary>
internal static class PgLiveness
{
    /// <summary>
    /// Gets the state of a record or a grant at an instant.
    /// </summary>
    /// <param name="deadline">The instant the record expires, or null when it doesn't.</param>
    /// <param name="session">The id of the session the record is bound to, or null when it is bound to none.</param>
    /// <param name="sessionDeadline">The deadline of the session's row, or null when the row is gone.</param>
    /// <param name="now">The instant, on the server's clock.</param>
    /// <returns><see cref="PgRecordState.Expired"/> when the record's deadline is at or before the instant; else
    /// <see cref="PgRecordState.SessionEnded"/> when it is bound and the session's row is gone; else
    /// <see cref="PgRecordState.SessionExpired"/> when the session's deadline is at or before the instant; else
    /// <see cref="PgRecordState.Live"/>.</returns>
    public static PgRecordState StateOf(
        DateTime? deadline,
        string? session,
        DateTime? sessionDeadline,
        DateTime now
    )
    {
        if (deadline is { } own && own <= now)
        {
            return PgRecordState.Expired;
        }

        if (session is null)
        {
            return PgRecordState.Live;
        }

        if (sessionDeadline is not { } bound)
        {
            return PgRecordState.SessionEnded;
        }

        return bound <= now ? PgRecordState.SessionExpired : PgRecordState.Live;
    }
}
