// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessClaimServiceTests
{
    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_WhenTheNameHasTheMostSegments_ThrowsArgumentException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessClaimService(InProcessTesting.CreateTime());
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = InProcessTesting.Keyspace.CreateKey(segments);
        var window = new TimeWindow(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1));
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, null, cancellationToken)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task TryClaimAsync_WhenWindowsShareTheirStart_ShareTheClaim()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new InProcessClaimService(InProcessTesting.CreateTime());
        var name = InProcessTesting.Keyspace.CreateKey("job");
        var oneHour = new TimeWindow(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1));
        var twoHours = new TimeWindow(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(2));
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var first = await service.TryClaimAsync(name, oneHour, timeToLive, null, cancellationToken);

        var second = await service.TryClaimAsync(
            name,
            twoHours,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, first);
        Assert.Equal(ClaimStatus.Taken, second);
    }

    #endregion
}
