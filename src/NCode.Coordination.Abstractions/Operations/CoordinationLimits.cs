// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The limits every provider accepts.
/// </summary>
/// <remarks>
/// A provider whose server keeps less than a limit declares the primitive unsupported.
/// </remarks>
public static class CoordinationLimits
{
    /// <summary>
    /// The longest owner token, session token or node id, in characters.
    /// </summary>
    public const int MaxTokenLength = 128;

    /// <summary>
    /// The longest continuation token of a listing, in characters.
    /// </summary>
    public const int MaxContinuationTokenLength = 1_024;

    /// <summary>
    /// The longest value of a record or of a leader, in bytes.
    /// </summary>
    public const int MaxValueLength = 65_536;

    /// <summary>
    /// The longest payload of a notification, in bytes.
    /// </summary>
    public const int MaxPayloadLength = 4_096;

    /// <summary>
    /// The longest address of a member, in characters.
    /// </summary>
    public const int MaxAddressLength = 256;

    /// <summary>
    /// The most metadata entries of a member.
    /// </summary>
    public const int MaxMetadataEntries = 64;

    /// <summary>
    /// The longest value of a metadata entry, in characters.
    /// </summary>
    public const int MaxMetadataValueLength = 512;

    /// <summary>
    /// The most extensions in the options of one call.
    /// </summary>
    public const int MaxExtensions = 32;

    /// <summary>
    /// The longest value of an extension, in characters.
    /// </summary>
    public const int MaxExtensionValueLength = 1_024;

    /// <summary>
    /// The most records in one page of a listing.
    /// </summary>
    public const int MaxPageSize = 1_000;

    /// <summary>
    /// The most conditions of a transaction.
    /// </summary>
    public const int MaxTransactionConditions = 64;

    /// <summary>
    /// The most writes of one branch of a transaction.
    /// </summary>
    public const int MaxTransactionWrites = 64;

    /// <summary>
    /// The most increments of one counter batch.
    /// </summary>
    public const int MaxBatchIncrements = 16;

    /// <summary>
    /// The greatest capacity of a semaphore.
    /// </summary>
    public const int MaxSemaphoreCapacity = 1_024;

    /// <summary>
    /// The most numbers one sequence reservation takes.
    /// </summary>
    public const int MaxSequenceBlock = 65_536;

    /// <summary>
    /// The most members of a set.
    /// </summary>
    public const int MaxSetMembers = 10_000;
}
