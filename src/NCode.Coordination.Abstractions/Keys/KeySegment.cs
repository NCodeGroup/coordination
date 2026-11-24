// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace NCode.Coordination;

/// <summary>
/// One typed part of a key or of a keyspace: text, a number or a UUID.
/// </summary>
/// <remarks>
/// A segment of one kind never equals a segment of another kind, so the text "1" and the number 1 are different
/// segments. Create one with <see cref="FromString"/>, <see cref="FromInt64"/> or <see cref="FromGuid"/>, or through
/// the implicit conversions from <see cref="string"/>, <see cref="long"/> and <see cref="Guid"/>.
/// </remarks>
public sealed record KeySegment
{
    /// <summary>
    /// The longest text segment, in UTF-16 code units.
    /// </summary>
    public const int MaxTextLength = 256;

    private static UTF8Encoding StrictUtf8 { get; } =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private KeySegment(KeySegmentKind kind, string? text, long? number, Guid? uuid)
    {
        Kind = kind;
        Text = text;
        Number = number;
        Uuid = uuid;
    }

    /// <summary>
    /// Gets the type of the value this segment holds.
    /// </summary>
    public KeySegmentKind Kind { get; }

    /// <summary>
    /// Gets the text of a <see cref="KeySegmentKind.Text"/> segment, and null for any other kind.
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// Gets the value of a <see cref="KeySegmentKind.Number"/> segment, and null for any other kind.
    /// </summary>
    public long? Number { get; }

    /// <summary>
    /// Gets the value of a <see cref="KeySegmentKind.Uuid"/> segment, and null for any other kind.
    /// </summary>
    public Guid? Uuid { get; }

    /// <summary>
    /// Creates a text segment.
    /// </summary>
    /// <param name="text">
    /// The text: 1 to <see cref="MaxTextLength"/> UTF-16 code units of well-formed UTF-16, with no unpaired surrogate.
    /// </param>
    /// <returns>A segment of kind <see cref="KeySegmentKind.Text"/>.</returns>
    public static KeySegment FromString(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException(
                $"A text segment is at most {MaxTextLength} characters, and this one has {text.Length}.",
                nameof(text)
            );
        }

        try
        {
            _ = StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A text segment is well-formed UTF-16, with no unpaired surrogate.",
                nameof(text),
                exception
            );
        }

        return new KeySegment(KeySegmentKind.Text, text, null, null);
    }

    /// <summary>
    /// Creates a number segment.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>A segment of kind <see cref="KeySegmentKind.Number"/>.</returns>
    public static KeySegment FromInt64(long value) => new(KeySegmentKind.Number, null, value, null);

    /// <summary>
    /// Creates a UUID segment.
    /// </summary>
    /// <param name="value">The UUID.</param>
    /// <returns>A segment of kind <see cref="KeySegmentKind.Uuid"/>.</returns>
    public static KeySegment FromGuid(Guid value) => new(KeySegmentKind.Uuid, null, null, value);

    /// <summary>
    /// Converts text to a text segment, as <see cref="FromString"/> does.
    /// </summary>
    /// <param name="text">The text.</param>
    public static implicit operator KeySegment(string text) => FromString(text);

    /// <summary>
    /// Converts a number to a number segment, as <see cref="FromInt64"/> does.
    /// </summary>
    /// <param name="value">The number.</param>
    public static implicit operator KeySegment(long value) => FromInt64(value);

    /// <summary>
    /// Converts a UUID to a UUID segment, as <see cref="FromGuid"/> does.
    /// </summary>
    /// <param name="value">The UUID.</param>
    public static implicit operator KeySegment(Guid value) => FromGuid(value);

    /// <summary>
    /// Returns the segment's value as text: the text itself, the number in invariant culture, or the UUID in the
    /// hyphenated "D" format.
    /// </summary>
    /// <returns>The segment's value as text.</returns>
    public override string ToString() =>
        Kind switch
        {
            KeySegmentKind.Text => Text ?? string.Empty,
            KeySegmentKind.Number => Number
                .GetValueOrDefault()
                .ToString(CultureInfo.InvariantCulture),
            _ => Uuid.GetValueOrDefault().ToString("D", CultureInfo.InvariantCulture),
        };
}
