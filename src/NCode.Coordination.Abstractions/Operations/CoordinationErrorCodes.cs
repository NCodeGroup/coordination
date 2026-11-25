// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The known codes of a <see cref="CoordinationException"/>, not all of them: a provider may use other names.
/// </summary>
public static class CoordinationErrorCodes
{
    /// <summary>
    /// The provider could not be reached or did not answer in time.
    /// </summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// The provider does not support what the call asks.
    /// </summary>
    public const string Unsupported = "unsupported";

    /// <summary>
    /// The call exceeds a limit of the provider's server.
    /// </summary>
    public const string LimitExceeded = "limit-exceeded";

    /// <summary>
    /// The server refused the provider's credentials.
    /// </summary>
    public const string AccessDenied = "access-denied";
}
