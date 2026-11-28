// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// An entry of a numbered log: a keyspace's change log or a topic's message log.
/// </summary>
internal interface IPgLogEntry
{
    /// <summary>
    /// Gets the entry's number: the revision of a change or the sequence of a message.
    /// </summary>
    long Number { get; }
}
