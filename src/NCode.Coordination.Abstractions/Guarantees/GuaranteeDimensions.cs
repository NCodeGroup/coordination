// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The names of the known dimensions a guarantee belongs to, not all of them.
/// </summary>
/// <remarks>
/// A dimension groups guarantees that answer one question about a provider. A provider or a deployment may define a
/// dimension of its own, so a dimension that is not listed here is not an error.
/// </remarks>
public static class GuaranteeDimensions
{
    /// <summary>
    /// The dimension of how a read relates to the writes before it.
    /// </summary>
    public const string Consistency = "consistency";

    /// <summary>
    /// The dimension of what stored data survives.
    /// </summary>
    public const string Durability = "durability";

    /// <summary>
    /// The dimension of how an expiry is measured.
    /// </summary>
    public const string Expiry = "expiry";

    /// <summary>
    /// The dimension of how often a notification reaches its receivers.
    /// </summary>
    public const string Delivery = "delivery";
}
