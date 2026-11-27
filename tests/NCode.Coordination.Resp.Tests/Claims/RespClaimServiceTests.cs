// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Claims.Tests;

public sealed class RespClaimServiceTests
{
    private static RespServer CreateUnreachableServer()
    {
        var failure = new InvalidOperationException("The test reaches no server.");
        var connection = new RespConnection(
            _ => ValueTask.FromException<IConnectionMultiplexer>(failure),
            owned: false
        );

        return new RespServer(connection);
    }

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_WhenTheNameHasTheMostSegments_ThrowsArgumentException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new RespClaimService(CreateUnreachableServer());
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);
        var keyspace = new Keyspace("resp-tests", segment);
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = keyspace.CreateKey(segments);
        var hour = TimeSpan.FromHours(1);
        var window = new TimeWindow(DateTimeOffset.UnixEpoch, hour);
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, null, cancellationToken)
        );

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
