// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Issues tokens from 128 bits of <see cref="RandomNumberGenerator"/> output.
/// </summary>
internal sealed class RespRandomTokens : IRespTokens
{
    /// <summary>The number of hexadecimal digits that carry 128 bits.</summary>
    private const int HexDigits = 32;

    /// <inheritdoc />
    public string Next(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return prefix + RandomNumberGenerator.GetHexString(HexDigits, lowercase: true);
    }
}
