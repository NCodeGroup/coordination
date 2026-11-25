// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The operations of the built-in primitives.
/// </summary>
/// <remarks>
/// These are the well-known operations, not all of them: a provider or a deployment may define operations of its own.
/// </remarks>
public static class Operations
{
    /// <summary>
    /// Gets the operation that opens a session.
    /// </summary>
    public static Operation SessionOpen { get; } = new("session.open");

    /// <summary>
    /// Gets the operation that renews a session.
    /// </summary>
    public static Operation SessionKeepAlive { get; } = new("session.keep-alive");

    /// <summary>
    /// Gets the operation that closes a session.
    /// </summary>
    public static Operation SessionClose { get; } = new("session.close");

    /// <summary>
    /// Gets the operation that waits for a session to end.
    /// </summary>
    public static Operation SessionWaitForEnd { get; } = new("session.wait-for-end");

    /// <summary>
    /// Gets the operation that adds a node to a group.
    /// </summary>
    public static Operation MembershipJoin { get; } = new("membership.join");

    /// <summary>
    /// Gets the operation that removes a session from a group.
    /// </summary>
    public static Operation MembershipLeave { get; } = new("membership.leave");

    /// <summary>
    /// Gets the operation that reads the members of a group.
    /// </summary>
    public static Operation MembershipRead { get; } = new("membership.read");

    /// <summary>
    /// Gets the operation that watches the members of a group.
    /// </summary>
    public static Operation MembershipWatch { get; } = new("membership.watch");

    /// <summary>
    /// Gets the operation that tries to acquire a lease.
    /// </summary>
    public static Operation LeaseTryAcquire { get; } = new("lease.try-acquire");

    /// <summary>
    /// Gets the operation that renews a lease.
    /// </summary>
    public static Operation LeaseRenew { get; } = new("lease.renew");

    /// <summary>
    /// Gets the operation that releases a lease.
    /// </summary>
    public static Operation LeaseRelease { get; } = new("lease.release");

    /// <summary>
    /// Gets the operation that waits for a lease to be released.
    /// </summary>
    public static Operation LeaseWaitForRelease { get; } = new("lease.wait-for-release");

    /// <summary>
    /// Gets the operation that tries to acquire a semaphore slot.
    /// </summary>
    public static Operation SemaphoreTryAcquire { get; } = new("semaphore.try-acquire");

    /// <summary>
    /// Gets the operation that renews a semaphore slot.
    /// </summary>
    public static Operation SemaphoreRenew { get; } = new("semaphore.renew");

    /// <summary>
    /// Gets the operation that releases a semaphore slot.
    /// </summary>
    public static Operation SemaphoreRelease { get; } = new("semaphore.release");

    /// <summary>
    /// Gets the operation that waits for a semaphore slot to be released.
    /// </summary>
    public static Operation SemaphoreWaitForRelease { get; } = new("semaphore.wait-for-release");

    /// <summary>
    /// Gets the operation that tries to take the leadership of an election.
    /// </summary>
    public static Operation ElectionTryLead { get; } = new("election.try-lead");

    /// <summary>
    /// Gets the operation that renews a leadership.
    /// </summary>
    public static Operation ElectionRenew { get; } = new("election.renew");

    /// <summary>
    /// Gets the operation that gives up a leadership.
    /// </summary>
    public static Operation ElectionResign { get; } = new("election.resign");

    /// <summary>
    /// Gets the operation that reads the current leader of an election.
    /// </summary>
    public static Operation ElectionRead { get; } = new("election.read");

    /// <summary>
    /// Gets the operation that watches the leadership of an election.
    /// </summary>
    public static Operation ElectionWatch { get; } = new("election.watch");

    /// <summary>
    /// Gets the operation that tries to claim a name for a time window.
    /// </summary>
    public static Operation ClaimTryClaim { get; } = new("claim.try-claim");

    /// <summary>
    /// Gets the operation that creates a record.
    /// </summary>
    public static Operation RecordCreate { get; } = new("record.create");

    /// <summary>
    /// Gets the operation that reads a record.
    /// </summary>
    public static Operation RecordRead { get; } = new("record.read");

    /// <summary>
    /// Gets the operation that replaces a record when its revision matches.
    /// </summary>
    public static Operation RecordCompareAndSwap { get; } = new("record.compare-and-swap");

    /// <summary>
    /// Gets the operation that deletes a record when its revision matches.
    /// </summary>
    public static Operation RecordCompareAndDelete { get; } = new("record.compare-and-delete");

    /// <summary>
    /// Gets the operation that reads and deletes a record in one step.
    /// </summary>
    public static Operation RecordTake { get; } = new("record.take");

    /// <summary>
    /// Gets the operation that lists records.
    /// </summary>
    public static Operation RecordList { get; } = new("record.list");

    /// <summary>
    /// Gets the operation that watches one record.
    /// </summary>
    public static Operation RecordWatchKey { get; } = new("record-watch.watch-key");

    /// <summary>
    /// Gets the operation that watches the records under a prefix.
    /// </summary>
    public static Operation RecordWatchPrefix { get; } = new("record-watch.watch-prefix");

    /// <summary>
    /// Gets the operation that runs a transaction.
    /// </summary>
    public static Operation TransactionExecute { get; } = new("transaction.execute");

    /// <summary>
    /// Gets the operation that tries to record an occurrence.
    /// </summary>
    public static Operation OccurrenceTryRecord { get; } = new("occurrence.try-record");

    /// <summary>
    /// Gets the operation that adds a member to a set.
    /// </summary>
    public static Operation SetAdd { get; } = new("set.add");

    /// <summary>
    /// Gets the operation that removes a member from a set.
    /// </summary>
    public static Operation SetRemove { get; } = new("set.remove");

    /// <summary>
    /// Gets the operation that lists the members of a set.
    /// </summary>
    public static Operation SetList { get; } = new("set.list");

    /// <summary>
    /// Gets the operation that removes every member of a set.
    /// </summary>
    public static Operation SetClear { get; } = new("set.clear");

    /// <summary>
    /// Gets the operation that adds to a counter.
    /// </summary>
    public static Operation CounterIncrement { get; } = new("counter.increment");

    /// <summary>
    /// Gets the operation that reads a counter.
    /// </summary>
    public static Operation CounterRead { get; } = new("counter.read");

    /// <summary>
    /// Gets the operation that resets a counter.
    /// </summary>
    public static Operation CounterReset { get; } = new("counter.reset");

    /// <summary>
    /// Gets the operation that adds to several counters at once.
    /// </summary>
    public static Operation CounterIncrementBatch { get; } = new("counter.increment-batch");

    /// <summary>
    /// Gets the operation that takes the next number of a sequence.
    /// </summary>
    public static Operation SequenceNext { get; } = new("sequence.next");

    /// <summary>
    /// Gets the operation that reserves a block of numbers of a sequence.
    /// </summary>
    public static Operation SequenceReserve { get; } = new("sequence.reserve");

    /// <summary>
    /// Gets the operation that publishes a notification.
    /// </summary>
    public static Operation NotificationPublish { get; } = new("notification.publish");

    /// <summary>
    /// Gets the operation that subscribes to notifications.
    /// </summary>
    public static Operation NotificationSubscribe { get; } = new("notification.subscribe");

    /// <summary>
    /// Gets the definition of each built-in operation, with the guarantee dimensions it carries.
    /// </summary>
    public static ImmutableArray<OperationDefinition> Definitions { get; } =
    [
        new OperationDefinition(
            SessionOpen,
            Primitives.Session,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            SessionKeepAlive,
            Primitives.Session,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(SessionClose, Primitives.Session, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            SessionWaitForEnd,
            Primitives.Session,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            MembershipJoin,
            Primitives.Membership,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            MembershipLeave,
            Primitives.Membership,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            MembershipRead,
            Primitives.Membership,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            MembershipWatch,
            Primitives.Membership,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            LeaseTryAcquire,
            Primitives.Lease,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            LeaseRenew,
            Primitives.Lease,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(LeaseRelease, Primitives.Lease, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            LeaseWaitForRelease,
            Primitives.Lease,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            SemaphoreTryAcquire,
            Primitives.Semaphore,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            SemaphoreRenew,
            Primitives.Semaphore,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            SemaphoreRelease,
            Primitives.Semaphore,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            SemaphoreWaitForRelease,
            Primitives.Semaphore,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            ElectionTryLead,
            Primitives.Election,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            ElectionRenew,
            Primitives.Election,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            ElectionResign,
            Primitives.Election,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(ElectionRead, Primitives.Election, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            ElectionWatch,
            Primitives.Election,
            GuaranteeDimensions.Consistency
        ),
        new OperationDefinition(
            ClaimTryClaim,
            Primitives.Claim,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            RecordCreate,
            Primitives.Record,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(RecordRead, Primitives.Record, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            RecordCompareAndSwap,
            Primitives.Record,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            RecordCompareAndDelete,
            Primitives.Record,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(
            RecordTake,
            Primitives.Record,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(RecordList, Primitives.Record, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            RecordWatchKey,
            Primitives.RecordWatch,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Delivery
        ),
        new OperationDefinition(
            RecordWatchPrefix,
            Primitives.RecordWatch,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Delivery
        ),
        new OperationDefinition(
            TransactionExecute,
            Primitives.Transaction,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry,
            GuaranteeDimensions.Atomicity
        ),
        new OperationDefinition(
            OccurrenceTryRecord,
            Primitives.Occurrence,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            SetAdd,
            Primitives.Set,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(
            SetRemove,
            Primitives.Set,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(SetList, Primitives.Set, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            SetClear,
            Primitives.Set,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(
            CounterIncrement,
            Primitives.Counter,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry
        ),
        new OperationDefinition(CounterRead, Primitives.Counter, GuaranteeDimensions.Consistency),
        new OperationDefinition(
            CounterReset,
            Primitives.Counter,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(
            CounterIncrementBatch,
            Primitives.Counter,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry,
            GuaranteeDimensions.Atomicity
        ),
        new OperationDefinition(
            SequenceNext,
            Primitives.Sequence,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(
            SequenceReserve,
            Primitives.Sequence,
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability
        ),
        new OperationDefinition(
            NotificationPublish,
            Primitives.Notification,
            GuaranteeDimensions.Delivery
        ),
        new OperationDefinition(
            NotificationSubscribe,
            Primitives.Notification,
            GuaranteeDimensions.Delivery
        ),
    ];
}
