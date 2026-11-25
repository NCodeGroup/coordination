// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call that publishes a message to a notification topic, which a history records.
/// </summary>
/// <param name="Payload">The text of the payload that the call publishes.</param>
internal sealed record NotificationCall(string Payload)
{
    /// <summary>
    /// The payload of the message that the test publishes after the run, which each subscriber waits for.
    /// </summary>
    public const string Last = "last";
}
