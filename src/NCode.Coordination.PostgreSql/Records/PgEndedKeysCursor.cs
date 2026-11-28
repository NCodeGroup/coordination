// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The place in the keys still bound to an ended session that the next page starts after.
/// </summary>
/// <param name="Session">The ended session's id.</param>
/// <param name="Revision">The revision of the entry that ended the session.</param>
/// <param name="AfterIndex">The index value to read after.</param>
/// <param name="AfterDepth">The partition depth to read after.</param>
internal readonly record struct PgEndedKeysCursor(
    string Session,
    long Revision,
    byte[] AfterIndex,
    short AfterDepth
)
{
    /// <summary>
    /// Creates the cursor that starts a session's first page.
    /// </summary>
    /// <param name="session">The ended session's id.</param>
    /// <param name="revision">The revision of the entry that ended the session.</param>
    /// <returns>A cursor before every key of the session.</returns>
    public static PgEndedKeysCursor First(string session, long revision)
    {
        var noIndex = Array.Empty<byte>();
        return new PgEndedKeysCursor(session, revision, noIndex, -1);
    }

    /// <summary>
    /// Creates the cursor that follows the last key of a page.
    /// </summary>
    /// <param name="last">The last key of the page.</param>
    /// <returns>A cursor for the same session and revision, after <paramref name="last"/>.</returns>
    public PgEndedKeysCursor After(PgChangeItem last)
    {
        return this with { AfterIndex = last.Index, AfterDepth = last.Depth };
    }
}
