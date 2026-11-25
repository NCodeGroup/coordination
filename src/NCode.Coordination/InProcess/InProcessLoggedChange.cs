// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A change to a record kept in an in-process change log, without the record's value.
/// </summary>
/// <param name="Key">The key of the record that changed.</param>
/// <param name="Revision">The revision of the keyspace at which the change happened.</param>
/// <param name="IsDelete">A value indicating whether the change deleted the record, rather than wrote it.</param>
internal readonly record struct InProcessLoggedChange(
    CoordinationKey Key,
    Revision Revision,
    bool IsDelete
);
