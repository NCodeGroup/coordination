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
        var time = InProcessTesting.CreateTime();
        var service = new InProcessClaimService(time);
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = InProcessTesting.Keyspace.CreateKey(segments);
        var hour = TimeSpan.FromHours(1);
        var window = new TimeWindow(DateTimeOffset.UnixEpoch, hour);
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, null, cancellationToken)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task TryClaimAsync_WhenWindowsShareTheirStart_ShareTheClaim()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var service = new InProcessClaimService(time);
        var name = InProcessTesting.Keyspace.CreateKey("job");
        var hour = TimeSpan.FromHours(1);
        var twoHourLength = TimeSpan.FromHours(2);
        var oneHour = new TimeWindow(DateTimeOffset.UnixEpoch, hour);
        var twoHours = new TimeWindow(DateTimeOffset.UnixEpoch, twoHourLength);
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);
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
