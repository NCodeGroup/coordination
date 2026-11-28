// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Logging;

/// <summary>
/// Names the event ids of the PostgreSQL provider's log messages, which are append-only.
/// </summary>
internal static class EventIds
{
    /// <summary>
    /// The first id of the PostgreSQL provider's band, which spans 4000 to 4999.
    /// </summary>
    public const int Base = 4000;

    /// <summary>
    /// A whole sweep of the expired rows failed unexpectedly.
    /// </summary>
    public const int SweepFailed = Base + 1;

    /// <summary>
    /// One pass of the sweep failed.
    /// </summary>
    public const int SweepPassFailed = Base + 2;

    /// <summary>
    /// One step of the sweep failed.
    /// </summary>
    public const int SweepStepFailed = Base + 3;

    /// <summary>
    /// Reading a log for its subscribers failed with a coordination failure.
    /// </summary>
    public const int PumpFailed = Base + 4;

    /// <summary>
    /// Reading a log for its subscribers failed unexpectedly.
    /// </summary>
    public const int PumpFaulted = Base + 5;

    /// <summary>
    /// The listening connection failed.
    /// </summary>
    public const int ListenerConnectionFailed = Base + 6;

    /// <summary>
    /// Disposing a broken listening connection failed.
    /// </summary>
    public const int ListenerDisposeFailed = Base + 7;

    /// <summary>
    /// One pass of the sweep failed unexpectedly.
    /// </summary>
    public const int SweepPassFaulted = Base + 8;

    /// <summary>
    /// One step of the sweep failed unexpectedly.
    /// </summary>
    public const int SweepStepFaulted = Base + 9;
}
