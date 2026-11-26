// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// Checks the host's options against the rules the contracts enforce, so a bad value stops the host as it starts.
/// </summary>
internal class CoordinationHostOptionsValidator : IValidateOptions<CoordinationHostOptions>
{
    /// <summary>
    /// Checks the options and reports every property that breaks its rule.
    /// </summary>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="options">The options to check.</param>
    /// <returns>Success, or a failure that lists each invalid property.</returns>
    public ValidateOptionsResult Validate(string? name, CoordinationHostOptions options)
    {
        var result = HostSessionSettings.Create(options);

        return result.Failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Failures);
    }
}
