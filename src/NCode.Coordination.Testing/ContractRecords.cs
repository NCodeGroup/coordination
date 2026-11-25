// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The record helpers that more than one contract suite uses.
/// </summary>
internal static class ContractRecords
{
    /// <summary>
    /// Creates a persistent record that must not exist yet and returns the revision it was written at.
    /// </summary>
    /// <param name="records">The record service to write with.</param>
    /// <param name="key">The key to create.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>The revision of the record.</returns>
    public static async ValueTask<Revision> CreateAsync(
        IRecordService records,
        CoordinationKey key,
        byte[] value,
        CancellationToken cancellationToken
    )
    {
        var result = await records.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        Assert.True(result.IsWritten);
        return result.Revision;
    }
}
