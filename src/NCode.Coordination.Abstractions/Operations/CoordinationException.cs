// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The failure of an operation: the provider could not carry it out.
/// </summary>
/// <remarks>
/// The <see cref="Code"/> says what failed and never has to be recovered from the message.
/// <see cref="OutcomeUnknown"/> says whether a write may have taken effect anyway.
/// </remarks>
public sealed class CoordinationException : Exception
{
    /// <summary>
    /// Creates a failure.
    /// </summary>
    /// <param name="code">The code of the failure, a vocabulary name.</param>
    /// <param name="outcomeUnknown">True when the operation may have taken effect.</param>
    /// <param name="message">What failed.</param>
    /// <exception cref="ArgumentNullException">The code or the message is null.</exception>
    /// <exception cref="ArgumentException">The code is not a valid name, or the message is blank.</exception>
    public CoordinationException(string code, bool outcomeUnknown, string message)
        : this(code, outcomeUnknown, message, null) { }

    /// <summary>
    /// Creates a failure that another exception caused.
    /// </summary>
    /// <param name="code">The code of the failure, a vocabulary name.</param>
    /// <param name="outcomeUnknown">True when the operation may have taken effect.</param>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The exception that caused the failure, or null.</param>
    /// <exception cref="ArgumentNullException">The code or the message is null.</exception>
    /// <exception cref="ArgumentException">The code is not a valid name, or the message is blank.</exception>
    public CoordinationException(
        string code,
        bool outcomeUnknown,
        string message,
        Exception? innerException
    )
        : base(message, innerException)
    {
        Code = VocabularyName.Validate(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>
    /// Gets the code of the failure.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets a value indicating whether the operation may have taken effect.
    /// </summary>
    public bool OutcomeUnknown { get; }
}
