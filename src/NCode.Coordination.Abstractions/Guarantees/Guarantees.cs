// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The built-in guarantees, and their definitions.
/// </summary>
/// <remarks>
/// These are the well-known guarantees, not all of them: a provider or a deployment may define guarantees of its own.
/// </remarks>
public static class Guarantees
{
    /// <summary>
    /// Gets the guarantee that operations are linearizable and stay so across a failover of the server.
    /// </summary>
    public static Guarantee LinearizableAcrossFailover { get; } =
        new("consistency.linearizable-across-failover");

    /// <summary>
    /// Gets the guarantee that operations are linearizable while the server's primary serves them.
    /// </summary>
    public static Guarantee LinearizableUntilFailover { get; } =
        new("consistency.linearizable-until-failover");

    /// <summary>
    /// Gets the guarantee that operations are linearizable among the callers in one process.
    /// </summary>
    public static Guarantee LinearizableWithinProcess { get; } =
        new("consistency.linearizable-within-process");

    /// <summary>
    /// Gets the guarantee that an acknowledged write survives a crash of the server or of a minority of its replicas.
    /// </summary>
    public static Guarantee Durable { get; } = new("durability.durable");

    /// <summary>
    /// Gets the guarantee that an acknowledged write is copied to replicas after it is acknowledged.
    /// </summary>
    public static Guarantee ReplicatedAsynchronously { get; } =
        new("durability.replicated-asynchronously");

    /// <summary>
    /// Gets the guarantee that an acknowledged write lasts while the server runs.
    /// </summary>
    public static Guarantee Volatile { get; } = new("durability.volatile");

    /// <summary>
    /// Gets the guarantee that the server measures every duration.
    /// </summary>
    public static Guarantee ServerClock { get; } = new("expiry.server-clock");

    /// <summary>
    /// Gets the guarantee that durations are measured on the calling node's clock, or on the server's.
    /// </summary>
    public static Guarantee CallerClock { get; } = new("expiry.caller-clock");

    /// <summary>
    /// Gets the guarantee that a subscriber receives a notification at most once and is told to resync whenever it may
    /// have missed one.
    /// </summary>
    public static Guarantee AtMostOnceWithGapDetection { get; } =
        new("delivery.at-most-once-with-gap-detection");

    /// <summary>
    /// Gets the guarantee that a subscriber receives a notification at most once.
    /// </summary>
    public static Guarantee AtMostOnce { get; } = new("delivery.at-most-once");

    /// <summary>
    /// Gets the definition of each built-in guarantee, including the guarantees each one implies.
    /// </summary>
    public static ImmutableArray<GuaranteeDefinition> Definitions { get; } =
    [
        new GuaranteeDefinition(
            LinearizableAcrossFailover,
            GuaranteeDimensions.Consistency,
            "Operations are linearizable, and stay so when the server fails over: no acknowledged write is lost."
        )
        {
            Implies = [LinearizableUntilFailover],
        },
        new GuaranteeDefinition(
            LinearizableUntilFailover,
            GuaranteeDimensions.Consistency,
            "Operations are linearizable while the server's primary serves them; a failover can lose acknowledged writes."
        )
        {
            Implies = [LinearizableWithinProcess],
        },
        new GuaranteeDefinition(
            LinearizableWithinProcess,
            GuaranteeDimensions.Consistency,
            "Operations are linearizable among the callers in one process."
        ),
        new GuaranteeDefinition(
            Durable,
            GuaranteeDimensions.Durability,
            "An acknowledged write survives a crash of the server or of a minority of its replicas."
        )
        {
            Implies = [Volatile],
        },
        new GuaranteeDefinition(
            ReplicatedAsynchronously,
            GuaranteeDimensions.Durability,
            "An acknowledged write is copied to replicas after it is acknowledged, so losing the server loses only writes not yet copied."
        )
        {
            Implies = [Volatile],
        },
        new GuaranteeDefinition(
            Volatile,
            GuaranteeDimensions.Durability,
            "An acknowledged write lasts while the server runs; a restart can lose it."
        ),
        new GuaranteeDefinition(
            ServerClock,
            GuaranteeDimensions.Expiry,
            "The server measures every duration, so callers' clocks do not change when state expires."
        )
        {
            Implies = [CallerClock],
        },
        new GuaranteeDefinition(
            CallerClock,
            GuaranteeDimensions.Expiry,
            "Durations are measured on the calling node's clock, or on the server's."
        ),
        new GuaranteeDefinition(
            AtMostOnceWithGapDetection,
            GuaranteeDimensions.Delivery,
            "Each subscriber receives a notification at most once, and is told to resync whenever it may have missed one."
        )
        {
            Implies = [AtMostOnce],
        },
        new GuaranteeDefinition(
            AtMostOnce,
            GuaranteeDimensions.Delivery,
            "Each subscriber receives a notification at most once; one it misses can go unnoticed."
        ),
    ];
}
