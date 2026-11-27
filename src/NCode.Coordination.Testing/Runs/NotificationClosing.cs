// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// Tells what a subscriber of a notification run read after the run's closing publish: a resync carries the last
/// number it delivered, not the number it resynced at, so only one read after the publish returned surely covers the
/// publish.
/// </summary>
internal static class NotificationClosing
{
    /// <summary>
    /// Waits until a subscriber reads the closing message or a resync after the closing publish began, and tells whether
    /// that settles the publish.
    /// </summary>
    /// <param name="subscriber">The subscriber of the run.</param>
    /// <param name="closing">The recorded closing publish, whose input is a <see cref="NotificationCall"/>.</param>
    /// <param name="gapDetection">Whether the provider declares gap detection; without it a resync answers nothing.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>
    /// <see cref="ClosingOutcome.Closed"/> when the subscriber read the closing message or a resync after the publish
    /// returned; <see cref="ClosingOutcome.Unsettled"/> when the first answer is a resync read while the publish was in
    /// flight; <see cref="ClosingOutcome.Missing"/> when nothing answered before the subscriber's wait ended.
    /// </returns>
    /// <exception cref="ArgumentNullException">The subscriber or the publish is null.</exception>
    /// <exception cref="ArgumentException">The publish's input is not a <see cref="NotificationCall"/>.</exception>
    public static async ValueTask<ClosingOutcome> WaitAsync(
        RunSubscriber<Notification> subscriber,
        HistoryOperation closing,
        bool gapDetection,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(closing);
        if (closing.Input is not NotificationCall call)
        {
            throw new ArgumentException("The closing operation is not a publish.", nameof(closing));
        }

        bool Answers(Delivery<Notification> delivery)
        {
            if (delivery.Item.IsResync)
            {
                return gapDetection && delivery.Stamp > closing.Invoked;
            }

            var payload = HistoryValues.Decode(delivery.Item.Payload);
            return string.Equals(payload, call.Payload, StringComparison.Ordinal);
        }

        bool Settles(Delivery<Notification> delivery) =>
            Answers(delivery) && (!delivery.Item.IsResync || delivery.Stamp > closing.Responded);

        var answered = await subscriber.WaitForAsync(Answers, cancellationToken);
        if (!answered)
        {
            return ClosingOutcome.Missing;
        }

        var settled = subscriber.Received.Any(Settles);
        return settled ? ClosingOutcome.Closed : ClosingOutcome.Unsettled;
    }
}
