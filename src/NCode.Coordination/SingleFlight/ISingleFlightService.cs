// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Lets one caller fetch a value while every other caller for the same key waits for it, built on records and leases.
/// </summary>
/// <remarks>
/// The keys under the key belong to the service, so the key has at most 15 segments. Failures and cancellation throw
/// as the primitives do.
/// </remarks>
public interface ISingleFlightService
{
    /// <summary>
    /// Returns the value stored under a key, or fetches and stores it when it is absent.
    /// </summary>
    /// <remarks>
    /// One caller fetches the value while the others wait, and the stored value lives for the value time to live in
    /// the terms. A failed fetch lets the next caller fetch. A fetch that outlives the fetch time to live in the terms
    /// may overlap another. The value is at most <see cref="CoordinationLimits.MaxValueLength"/> bytes.
    /// </remarks>
    /// <param name="key">The key of the value.</param>
    /// <param name="terms">How long the value lives and how long a fetch holds its turn.</param>
    /// <param name="fetch">The function that fetches the value.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The stored record that holds the value.</returns>
    ValueTask<StoredRecord> GetOrFetchAsync(
        CoordinationKey key,
        SingleFlightTerms terms,
        Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>>> fetch,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
