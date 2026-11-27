// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// What a subscriber of a notification run read after the run's closing publish.
/// </summary>
internal enum ClosingOutcome
{
    /// <summary>
    /// The subscriber read neither the closing message nor a resync after the publish began before its wait ended.
    /// </summary>
    Missing,

    /// <summary>
    /// The first item that answered the publish is a resync read while the publish was in flight, which may or may not
    /// cover it.
    /// </summary>
    Unsettled,

    /// <summary>
    /// The subscriber read the closing message, or a resync after the publish returned.
    /// </summary>
    Closed,
}
