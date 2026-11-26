// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.Recipes.Tests;

/// <summary>
/// A record watch service whose streams end without a change or a failure, which the contracts never do.
/// </summary>
internal sealed class EndingWatches : IRecordWatchService
{
    public async IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await Task.Yield();

        yield break;
    }

    public async IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await Task.Yield();

        yield break;
    }
}
