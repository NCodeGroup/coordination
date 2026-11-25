// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// The connection of a <c>DroppingProvider</c> to its server, which a test drops: a drop starts an outage in which
/// the next calls fail as if they were in flight, and marks every live subscription.
/// </summary>
internal sealed class DroppingConnection
{
    /// <summary>
    /// The number of calls that fail in one outage.
    /// </summary>
    public const int FailedCalls = 3;

    /// <summary>
    /// Gets the lock that guards the outage and the subscriptions.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets or sets the number of calls left to fail in the outage.
    /// </summary>
    private int Remaining { get; set; }

    /// <summary>
    /// Gets or sets the number of calls failed so far.
    /// </summary>
    private int Failed { get; set; }

    /// <summary>
    /// Gets the live subscriptions.
    /// </summary>
    private List<DroppingSubscription> Subscriptions { get; } = [];

    /// <summary>
    /// Drops the connection: starts an outage and marks every live subscription. It takes no time and waits for
    /// nothing: the outage ends once its calls have failed.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the drop.</param>
    /// <returns>A task that is already complete.</returns>
    public ValueTask DropAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = Gate.EnterScope();

        Remaining = FailedCalls;

        foreach (var subscription in Subscriptions)
        {
            subscription.Drop();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Adds a live subscription that a drop marks.
    /// </summary>
    /// <returns>The new subscription.</returns>
    public DroppingSubscription Subscribe()
    {
        var subscription = new DroppingSubscription();

        using var scope = Gate.EnterScope();

        Subscriptions.Add(subscription);

        return subscription;
    }

    /// <summary>
    /// Removes a subscription, which a drop no longer marks.
    /// </summary>
    /// <param name="subscription">The subscription to remove.</param>
    public void Unsubscribe(DroppingSubscription subscription)
    {
        using var scope = Gate.EnterScope();

        Subscriptions.Remove(subscription);
    }

    /// <summary>
    /// Runs a call over the connection: in an outage it fails, the first and third call of the outage before it
    /// reaches the provider and the second after it applied.
    /// </summary>
    /// <typeparam name="T">The type of the call's result.</typeparam>
    /// <param name="call">The call to run.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The result of the call.</returns>
    /// <exception cref="CoordinationException">The connection dropped while the call was in flight.</exception>
    public async ValueTask<T> CallAsync<T>(
        Func<CancellationToken, ValueTask<T>> call,
        CancellationToken cancellationToken
    )
    {
        var fails = TryFail(out var applies);

        if (fails && !applies)
        {
            throw CreateFailure();
        }

        var result = await call(cancellationToken);

        if (fails)
        {
            throw CreateFailure();
        }

        return result;
    }

    /// <summary>
    /// Runs a call that returns nothing over the connection: in an outage it fails, the first and third call of the
    /// outage before it reaches the provider and the second after it applied.
    /// </summary>
    /// <param name="call">The call to run.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the call has.</returns>
    /// <exception cref="CoordinationException">The connection dropped while the call was in flight.</exception>
    public async ValueTask CallAsync(
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        var fails = TryFail(out var applies);

        if (fails && !applies)
        {
            throw CreateFailure();
        }

        await call(cancellationToken);

        if (fails)
        {
            throw CreateFailure();
        }
    }

    /// <summary>
    /// Takes one call of the outage, when there is one.
    /// </summary>
    /// <param name="applies">Whether the call reaches the provider and applies before it fails.</param>
    /// <returns><see langword="true" /> when the call fails.</returns>
    private bool TryFail(out bool applies)
    {
        using var scope = Gate.EnterScope();

        if (Remaining == 0)
        {
            applies = true;
            return false;
        }

        Remaining--;
        Failed++;
        applies = Failed % 2 == 0;
        return true;
    }

    /// <summary>
    /// Creates the failure of a call in flight when the connection dropped.
    /// </summary>
    /// <returns>An exception that tells the caller that the outcome is unknown.</returns>
    private static CoordinationException CreateFailure() =>
        new(
            CoordinationErrorCodes.Unavailable,
            true,
            "The connection dropped while the call was in flight."
        );
}
