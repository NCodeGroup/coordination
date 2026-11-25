// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Checks what each subscriber of a notification topic received against what the history published and against the
/// delivery that a provider declares.
/// </summary>
internal static class NotificationDeliveryChecker
{
    /// <summary>
    /// Checks the deliveries of a topic's subscribers.
    /// </summary>
    /// <param name="history">The calls of the run, among which the publishes are the <see cref="NotificationCall"/> calls.</param>
    /// <param name="subscribers">What each subscriber received, in the order it received it.</param>
    /// <param name="gapDetection">
    /// Whether the provider declares gap detection, which requires consecutive numbers or a resync between them.
    /// </param>
    /// <returns>A description of each violation found, empty when the deliveries are as declared.</returns>
    public static ImmutableArray<string> Check(
        IReadOnlyList<HistoryOperation> history,
        IReadOnlyList<ImmutableArray<Delivery<Notification>>> subscribers,
        bool gapDetection
    )
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(subscribers);

        var publishes = history
            .Where(operation =>
                operation.Input is NotificationCall && operation.Outcome != HistoryOutcome.Failed
            )
            .ToList();

        var payloadsByNumber = new Dictionary<long, string>();
        var numbersByPayload = new Dictionary<string, long>(StringComparer.Ordinal);
        var violations = ImmutableArray.CreateBuilder<string>();

        for (var subscriber = 0; subscriber < subscribers.Count; ++subscriber)
        {
            var last = 0L;
            var resynced = true;

            foreach (var delivery in subscribers[subscriber])
            {
                var notification = delivery.Item;

                if (notification.IsResync)
                {
                    if (gapDetection && notification.Sequence != last)
                    {
                        violations.Add(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Subscriber {subscriber} received a resync carrying {notification.Sequence} after message {last}."
                            )
                        );
                    }

                    resynced = true;
                    continue;
                }

                var sequence = notification.Sequence;
                var payload = HistoryValues.Decode(notification.Payload);

                var published = publishes.Any(operation =>
                    operation.Invoked < delivery.Stamp
                    && operation.Input is NotificationCall call
                    && string.Equals(call.Payload, payload, StringComparison.Ordinal)
                );

                if (!published)
                {
                    violations.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Subscriber {subscriber} received '{payload}', which no call published before it."
                        )
                    );
                }

                if (sequence <= last)
                {
                    violations.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Subscriber {subscriber} received message {sequence} after message {last}."
                        )
                    );
                }
                else if (gapDetection && !resynced && sequence != last + 1)
                {
                    violations.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Subscriber {subscriber} received message {sequence} after message {last} without a resync."
                        )
                    );
                }

                if (payloadsByNumber.TryGetValue(sequence, out var carried))
                {
                    if (!string.Equals(carried, payload, StringComparison.Ordinal))
                    {
                        violations.Add(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Message {sequence} carried '{carried}' and '{payload}'."
                            )
                        );
                    }
                }
                else
                {
                    payloadsByNumber[sequence] = payload;
                }

                if (numbersByPayload.TryGetValue(payload, out var numbered))
                {
                    if (numbered != sequence)
                    {
                        violations.Add(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"'{payload}' was numbered {numbered} and {sequence}."
                            )
                        );
                    }
                }
                else
                {
                    numbersByPayload[payload] = sequence;
                }

                last = sequence;
                resynced = false;
            }
        }

        return violations.ToImmutable();
    }
}
