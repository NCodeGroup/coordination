// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// The result of writing a record: written at a revision, in conflict, not found, or the session ended.
/// </summary>
/// <remarks>
/// Create a written result with <see cref="Written(Revision)"/>; <see cref="Conflict"/>, <see cref="NotFound"/> and
/// <see cref="SessionEnded"/> are shared.
/// </remarks>
public sealed class RecordWriteResult
{
    /// <summary>
    /// Gets the result for a record whose state differed from what the write required.
    /// </summary>
    public static RecordWriteResult Conflict { get; } = new(RecordWriteStatus.Conflict, null);

    /// <summary>
    /// Gets the result for a record that does not exist.
    /// </summary>
    public static RecordWriteResult NotFound { get; } = new(RecordWriteStatus.NotFound, null);

    /// <summary>
    /// Gets the result for a session that has ended.
    /// </summary>
    public static RecordWriteResult SessionEnded { get; } =
        new(RecordWriteStatus.SessionEnded, null);

    private RecordWriteResult(RecordWriteStatus status, Revision? revision)
    {
        Status = status;
        Revision = revision;
    }

    /// <summary>
    /// Gets the outcome of the write.
    /// </summary>
    public RecordWriteStatus Status { get; }

    /// <summary>
    /// Gets the revision of the write, or null when the record was not written.
    /// </summary>
    public Revision? Revision { get; }

    /// <summary>
    /// Gets a value indicating whether the record was written.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Revision))]
    public bool IsWritten => Status == RecordWriteStatus.Written;

    /// <summary>
    /// Creates the result for a written record.
    /// </summary>
    /// <param name="revision">The revision of the write.</param>
    /// <returns>The written result.</returns>
    /// <exception cref="ArgumentNullException">The revision is null.</exception>
    public static RecordWriteResult Written(Revision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return new RecordWriteResult(RecordWriteStatus.Written, revision);
    }
}
