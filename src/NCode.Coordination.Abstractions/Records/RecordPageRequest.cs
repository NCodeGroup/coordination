// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// Which page of a listing to read: how many records at most, and where the page before ended.
/// </summary>
public sealed class RecordPageRequest
{
    /// <summary>
    /// Creates a request for a page.
    /// </summary>
    /// <param name="limit">The most records the page holds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is below 1 or above the page size limit.</exception>
    public RecordPageRequest(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, CoordinationLimits.MaxPageSize);

        Limit = limit;
    }

    /// <summary>
    /// Gets the most records the page holds.
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// Gets the token of the page before, or null for the first page, which is the default.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid token.</exception>
    public string? ContinuationToken
    {
        get;
        init
        {
            field = value is null
                ? null
                : OpaqueToken.Validate(
                    value,
                    CoordinationLimits.MaxContinuationTokenLength,
                    nameof(ContinuationToken)
                );
        }
    }
}
