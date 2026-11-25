// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// One page of a listing of records, consistent on its own.
/// </summary>
public sealed class RecordPage
{
    /// <summary>
    /// Creates a page.
    /// </summary>
    /// <param name="revision">The revision of the keyspace the page reflects.</param>
    /// <param name="records">The records of the page, possibly none.</param>
    /// <exception cref="ArgumentNullException">The revision or the records are null.</exception>
    /// <exception cref="ArgumentException">A record is null.</exception>
    public RecordPage(Revision revision, params IEnumerable<StoredRecord> records)
    {
        ArgumentNullException.ThrowIfNull(revision);

        Revision = revision;
        Records = ElementList.Copy(records, nameof(records));
    }

    /// <summary>
    /// Gets the revision of the keyspace the page reflects.
    /// </summary>
    public Revision Revision { get; }

    /// <summary>
    /// Gets the records of the page, in the order the provider chose.
    /// </summary>
    public ImmutableArray<StoredRecord> Records { get; }

    /// <summary>
    /// Gets the token that reads the page after this one, or null on the last page, which is the default.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid token.</exception>
    public string? ContinuationToken
    {
        get;
        init
        {
            field = OpaqueToken.ValidateOptional(
                value,
                CoordinationLimits.MaxContinuationTokenLength,
                nameof(ContinuationToken)
            );
        }
    }
}
