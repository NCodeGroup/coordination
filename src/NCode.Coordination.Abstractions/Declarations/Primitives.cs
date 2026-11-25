// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The primitives the product defines.
/// </summary>
/// <remarks>
/// These are the well-known primitives, not all of them: a provider may extend the vocabulary with primitives of its
/// own.
/// </remarks>
public static class Primitives
{
    /// <summary>
    /// Gets the primitive of a node's session, which lives while the node keeps it alive.
    /// </summary>
    public static Primitive Session { get; } = new("session");

    /// <summary>
    /// Gets the primitive of the set of live sessions that make up a group.
    /// </summary>
    public static Primitive Membership { get; } = new("membership");

    /// <summary>
    /// Gets the primitive of a lease: exclusive ownership of a key for a limited time.
    /// </summary>
    public static Primitive Lease { get; } = new("lease");

    /// <summary>
    /// Gets the primitive of a semaphore: a limited number of holders of a key at once.
    /// </summary>
    public static Primitive Semaphore { get; } = new("semaphore");

    /// <summary>
    /// Gets the primitive of an election: one leader chosen among the candidates for a key.
    /// </summary>
    public static Primitive Election { get; } = new("election");

    /// <summary>
    /// Gets the primitive of a claim: a first-come reservation of a key.
    /// </summary>
    public static Primitive Claim { get; } = new("claim");

    /// <summary>
    /// Gets the primitive of a record: a stored value under a key, with a revision.
    /// </summary>
    public static Primitive Record { get; } = new("record");

    /// <summary>
    /// Gets the primitive of a record watch: a stream of the changes to records.
    /// </summary>
    public static Primitive RecordWatch { get; } = new("record-watch");

    /// <summary>
    /// Gets the primitive of a transaction: several record changes applied together or not at all.
    /// </summary>
    public static Primitive Transaction { get; } = new("transaction");

    /// <summary>
    /// Gets the primitive of an occurrence: a dated event recorded once under a key.
    /// </summary>
    public static Primitive Occurrence { get; } = new("occurrence");

    /// <summary>
    /// Gets the primitive of a set: a collection of distinct members under a key.
    /// </summary>
    public static Primitive Set { get; } = new("set");

    /// <summary>
    /// Gets the primitive of a counter: a number that callers add to under a key.
    /// </summary>
    public static Primitive Counter { get; } = new("counter");

    /// <summary>
    /// Gets the primitive of a sequence: numbers that increase under a key.
    /// </summary>
    public static Primitive Sequence { get; } = new("sequence");

    /// <summary>
    /// Gets the primitive of a notification: a message sent to the nodes that subscribed to a key.
    /// </summary>
    public static Primitive Notification { get; } = new("notification");

    /// <summary>
    /// Gets every built-in primitive.
    /// </summary>
    public static ImmutableArray<Primitive> All { get; } =
    [
        Session,
        Membership,
        Lease,
        Semaphore,
        Election,
        Claim,
        Record,
        RecordWatch,
        Transaction,
        Occurrence,
        Set,
        Counter,
        Sequence,
        Notification,
    ];
}
