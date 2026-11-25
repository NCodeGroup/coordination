// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The sets of the in-process provider.
/// </summary>
/// <param name="time">The clock the members' expiries are measured on.</param>
/// <remarks>
/// Every operation runs entirely while holding the service's lock, so it is linearizable in the process.
/// </remarks>
internal class InProcessSetService(TimeProvider time) : ISetService
{
    /// <summary>
    /// Gets the lock that every operation holds.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the clock the members' expiries are measured on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the members of every set, by the set's key.
    /// </summary>
    private Dictionary<CoordinationKey, InProcessExpiringTable<KeySegment, bool>> Sets { get; } =
    [];

    /// <summary>
    /// Gets or sets the number of members added since the sets were last swept.
    /// </summary>
    private int AddsSinceSweep { get; set; }

    /// <inheritdoc />
    public ValueTask<bool> AddAsync(
        CoordinationKey key,
        KeySegment member,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(timeToLive);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            if (!Sets.TryGetValue(key, out var members))
            {
                members = new InProcessExpiringTable<KeySegment, bool>(Time);
                Sets[key] = members;
            }

            var wasAbsent = !members.TryGet(member, out _);
            if (wasAbsent && members.Count >= CoordinationLimits.MaxSetMembers)
            {
                members.Sweep();
                if (members.Count >= CoordinationLimits.MaxSetMembers)
                {
                    throw new CoordinationException(
                        CoordinationErrorCodes.LimitExceeded,
                        outcomeUnknown: false,
                        $"A set holds at most {CoordinationLimits.MaxSetMembers} members."
                    );
                }
            }

            members.Set(member, true, InProcessExpiry.Begin(Time, timeToLive));

            AddsSinceSweep++;
            if (
                AddsSinceSweep
                >= Math.Max(InProcessExpiringTable<KeySegment, bool>.SweepFloor, Sets.Count)
            )
            {
                SweepSets();
                AddsSinceSweep = 0;
            }

            return ValueTask.FromResult(wasAbsent);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(
        CoordinationKey key,
        KeySegment member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            if (!Sets.TryGetValue(key, out var members))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(members.Remove(member));
        }
    }

    /// <inheritdoc />
    public ValueTask<ImmutableArray<KeySegment>> ListAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            if (!Sets.TryGetValue(key, out var members))
            {
                return ValueTask.FromResult(ImmutableArray<KeySegment>.Empty);
            }

            var live = members.ListLive();
            if (live.Count == 0)
            {
                Sets.Remove(key);
                return ValueTask.FromResult(ImmutableArray<KeySegment>.Empty);
            }

            return ValueTask.FromResult(live.Select(entry => entry.Key).ToImmutableArray());
        }
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            Sets.Remove(key);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Removes the ended members of every set, and the sets left without a member. The caller holds the lock.
    /// </summary>
    private void SweepSets()
    {
        List<CoordinationKey>? empty = null;
        foreach (var (key, members) in Sets)
        {
            members.Sweep();
            if (members.Count == 0)
            {
                (empty ??= []).Add(key);
            }
        }

        if (empty is null)
        {
            return;
        }

        foreach (var key in empty)
        {
            Sets.Remove(key);
        }
    }
}
