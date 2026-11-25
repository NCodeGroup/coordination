// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// Converts between the text that a history records and the bytes that a primitive stores.
/// </summary>
internal static class HistoryValues
{
    /// <summary>
    /// The encoding of recorded text, which throws on invalid bytes instead of substituting characters.
    /// </summary>
    public static UTF8Encoding Encoding { get; } =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes recorded text as the bytes that a primitive stores.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Encode(string text) => Encoding.GetBytes(text);

    /// <summary>
    /// Decodes the bytes that a primitive returned back into recorded text.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <returns>The decoded text.</returns>
    public static string Decode(ReadOnlyMemory<byte> bytes) => Encoding.GetString(bytes.Span);
}
