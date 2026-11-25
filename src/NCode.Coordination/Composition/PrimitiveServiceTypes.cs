// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace NCode.Coordination.Composition;

/// <summary>
/// The interface each built-in primitive is consumed through.
/// </summary>
internal static class PrimitiveServiceTypes
{
    private static FrozenDictionary<Primitive, Type> Types { get; } =
        new Dictionary<Primitive, Type>
        {
            [Primitives.Session] = typeof(ISessionService),
            [Primitives.Membership] = typeof(IMembershipService),
            [Primitives.Lease] = typeof(ILeaseService),
            [Primitives.Semaphore] = typeof(ISemaphoreService),
            [Primitives.Election] = typeof(IElectionService),
            [Primitives.Claim] = typeof(IClaimService),
            [Primitives.Record] = typeof(IRecordService),
            [Primitives.RecordWatch] = typeof(IRecordWatchService),
            [Primitives.Transaction] = typeof(ITransactionService),
            [Primitives.Occurrence] = typeof(IOccurrenceService),
            [Primitives.Set] = typeof(ISetService),
            [Primitives.Counter] = typeof(ICounterService),
            [Primitives.Sequence] = typeof(ISequenceService),
            [Primitives.Notification] = typeof(INotificationService),
        }.ToFrozenDictionary();

    /// <summary>
    /// Finds the interface a primitive is consumed through.
    /// </summary>
    /// <param name="primitive">The primitive.</param>
    /// <returns>The interface, or null when the primitive has no built-in interface.</returns>
    public static Type? Find(Primitive primitive) => Types.GetValueOrDefault(primitive);
}
