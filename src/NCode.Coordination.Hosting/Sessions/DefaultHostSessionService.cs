// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Coordination.Hosting.Logging;

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// Holds the one coordination session of the host: it opens the session as the host starts, joins the node to its
/// group, renews the session on its own clock, opens another as soon as it loses it, and closes it as the host stops.
/// </summary>
/// <param name="sessions">The operations that open, renew and close the session.</param>
/// <param name="membership">The operations that join the node to its group.</param>
/// <param name="options">The options that name the node and tune the session.</param>
/// <param name="time">The clock the session's renewals and deadline run on.</param>
/// <param name="logger">The logger.</param>
/// <remarks>
/// The service is a lifecycle service rather than a background service because the session must close after every
/// other hosted service has stopped: a lease that one of them holds is bound to the session. Its start never waits
/// for the session to open, so the host answers liveness probes while it is not ready.
/// </remarks>
internal class DefaultHostSessionService(
    ISessionService sessions,
    IMembershipService membership,
    IOptions<CoordinationHostOptions> options,
    TimeProvider time,
    ILogger<DefaultHostSessionService> logger
) : IHostSessionService, IHostedLifecycleService, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The message of the exception that a call raises once the host has stopped.
    /// </summary>
    private const string StoppedMessage =
        "The host has stopped, so it holds no coordination session.";

    /// <summary>
    /// Validates the options and builds the settings the service runs with.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException">The options are null.</exception>
    /// <exception cref="OptionsValidationException">A property of the options breaks its rule.</exception>
    private static HostSessionSettings CreateSettings(IOptions<CoordinationHostOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        return HostSessionSettings.Create(options.Value, failures)
            ?? throw new OptionsValidationException(
                Options.DefaultName,
                typeof(CoordinationHostOptions),
                failures
            );
    }

    /// <summary>
    /// Gets the operations that open, renew and close the session.
    /// </summary>
    private ISessionService Sessions { get; } = sessions;

    /// <summary>
    /// Gets the operations that join the node to its group.
    /// </summary>
    private IMembershipService Membership { get; } = membership;

    /// <summary>
    /// Gets the clock the session's renewals and deadline run on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the logger.
    /// </summary>
    private ILogger Logger { get; } = logger;

    /// <summary>
    /// Gets the validated settings the service runs with.
    /// </summary>
    private HostSessionSettings Settings { get; } = CreateSettings(options);

    /// <summary>
    /// Gets the lock that guards the state, the session, its end, the next-session source and the stop.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the source that tells the session loop the host is stopping.
    /// </summary>
    /// <remarks>
    /// It is never disposed. It holds no timer, so a stop and a disposal can run in any order and more than once.
    /// </remarks>
    private CancellationTokenSource Stopping { get; } = new();

    /// <summary>
    /// Gets or sets where the session stands. It is read and written under the gate only.
    /// </summary>
    private HostSessionState CurrentState { get; set; }

    /// <summary>
    /// Gets or sets the session the host holds, or null. It is read and written under the gate only.
    /// </summary>
    private HostSession? Session { get; set; }

    /// <summary>
    /// Gets or sets the source that ends the held session's token, or null. It is read and written under the gate only.
    /// </summary>
    private CancellationTokenSource? EndedSource { get; set; }

    /// <summary>
    /// Gets or sets the source that completes the waits for the next session. It is read and written under the gate
    /// only.
    /// </summary>
    private TaskCompletionSource<HostSession> Next { get; set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets or sets the task of the stop that is running or has run. It is read and written under the gate only.
    /// </summary>
    private Task? Stop { get; set; }

    /// <summary>
    /// Gets or sets the task of the session loop.
    /// </summary>
    private Task? Run { get; set; }

    /// <inheritdoc />
    public HostSessionState State
    {
        get
        {
            lock (Gate)
            {
                return CurrentState;
            }
        }
    }

    /// <inheritdoc />
    public HostSession? Current
    {
        get
        {
            lock (Gate)
            {
                return Session is { Ended.IsCancellationRequested: false } session ? session : null;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<HostSession> WaitForSessionAsync(CancellationToken cancellationToken)
    {
        Task<HostSession> next;

        lock (Gate)
        {
            if (CurrentState == HostSessionState.Stopped)
            {
                throw new InvalidOperationException(StoppedMessage);
            }

            if (Session is { Ended.IsCancellationRequested: false } session)
            {
                return ValueTask.FromResult(session);
            }

            next = Next.Task;
        }

        var wait = next.WaitAsync(cancellationToken);
        return new ValueTask<HostSession>(wait);
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            if (CurrentState != HostSessionState.NotStarted)
            {
                return Task.CompletedTask;
            }

            CurrentState = HostSessionState.Opening;
        }

        // Runs on this thread until its first wait that does not complete at once.
        Run = RunAsync(Stopping.Token).AsTask();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Gate)
        {
            if (Stop is { } running)
            {
                return running;
            }

            Stop = stopped.Task;
        }

        return StopCoreAsync(stopped, cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Stopping.CancelAsync().ConfigureAwait(false);

        if (Run is { } run)
        {
            await run.ConfigureAwait(false);
        }

        EndQuietly(TakeEnded());
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stopping.Cancel();
        EndQuietly(TakeEnded());
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Stops the session loop, ends the session's token and closes the session.
    /// </summary>
    /// <param name="stopped">The source that completes when the stop is over, whatever its outcome.</param>
    /// <param name="cancellationToken">The token that is cancelled when the host's shutdown timeout passes.</param>
    /// <returns>A task that completes when the stop is over.</returns>
    private async ValueTask StopCoreAsync(
        TaskCompletionSource stopped,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Stopping.CancelAsync().ConfigureAwait(false);

            // The loop ends without throwing once the host is stopping.
            if (Run is { } run)
            {
                await run.ConfigureAwait(false);
            }

            HostSession? session;
            CancellationTokenSource? ended;
            TaskCompletionSource<HostSession> next;

            lock (Gate)
            {
                session = Session;
                ended = EndedSource;
                next = Next;
                Session = null;
                EndedSource = null;
                CurrentState = HostSessionState.Stopped;
            }

            next.TrySetException(new InvalidOperationException(StoppedMessage));

            // Observes the exception, so a stop with no waiter raises no unobserved task exception.
            _ = next.Task.Exception;

            EndQuietly(ended);

            if (session is null)
            {
                return;
            }

            try
            {
                await Sessions
                    .CloseAsync(session.Id, null, cancellationToken)
                    .ConfigureAwait(false);
                Logger.SessionClosed();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Logger.SessionCloseTimedOut();
            }
            catch (Exception exception)
            {
                Logger.SessionCloseFailed(exception);
            }
        }
        finally
        {
            stopped.SetResult();
        }
    }

    /// <summary>
    /// Takes the source that ends the held session's token and forgets the session.
    /// </summary>
    /// <returns>The source, or null when no session is held.</returns>
    private CancellationTokenSource? TakeEnded()
    {
        lock (Gate)
        {
            var ended = EndedSource;
            EndedSource = null;
            Session = null;
            return ended;
        }
    }

    /// <summary>
    /// Opens a session, holds it until it is lost, and tries again, until the host stops.
    /// </summary>
    /// <param name="stopping">The token that is cancelled when the host stops.</param>
    /// <returns>A task that completes once the host is stopping.</returns>
    private async ValueTask RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                // Started before the session opens, so the first renewal is due one interval after its deadline began.
                using var timer = new PeriodicTimer(Settings.KeepAliveInterval, Time);
                var session = await OpenAsync(stopping).ConfigureAwait(false);

                if (session is not null)
                {
                    await HoldAsync(session, timer, stopping).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(Settings.RetryDelay, Time, stopping).ConfigureAwait(false);
            }
            catch (Exception) when (stopping.IsCancellationRequested)
            {
                // The stop closes the session that is held.
                return;
            }
            catch (Exception exception)
            {
                // The loop ends, and the stop still ends and closes the session that is held.
                Logger.SessionLoopFailed(exception);
                SetState(HostSessionState.Unreachable);
                return;
            }
        }
    }

    /// <summary>
    /// Opens a session and joins the node to its group.
    /// </summary>
    /// <param name="stopping">The token that is cancelled when the host stops.</param>
    /// <returns>The session, published to the waiting callers, or null when it could not be opened and joined.</returns>
    private async ValueTask<HostSession?> OpenAsync(CancellationToken stopping)
    {
        var deadline = HolderDeadline.Begin(Time, Settings.TimeToLive, Settings.DriftAllowance);
        SessionGrant grant;

        try
        {
            grant = await Sessions
                .OpenAsync(Settings.Keyspace, Settings.TimeToLive, null, stopping)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!stopping.IsCancellationRequested)
        {
            Logger.SessionOpenFailed(exception, Settings.RetryDelay);
            SetState(HostSessionState.Unreachable);
            return null;
        }

        var member = Settings.CreateMember(grant.Id);
        MembershipJoinStatus status;

        try
        {
            status = await Membership
                .JoinAsync(Settings.Group, member, null, stopping)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!stopping.IsCancellationRequested)
        {
            Logger.SessionOpenFailed(exception, Settings.RetryDelay);
            SetState(HostSessionState.Unreachable);
            await CloseQuietlyAsync(grant.Id, stopping).ConfigureAwait(false);
            return null;
        }

        if (status == MembershipJoinStatus.NodeIdTaken)
        {
            Logger.NodeIdTaken(Settings.NodeId, Settings.RetryDelay);
            SetState(HostSessionState.Opening);
            await CloseQuietlyAsync(grant.Id, stopping).ConfigureAwait(false);
            return null;
        }

        if (status != MembershipJoinStatus.Joined)
        {
            // The session ended before it joined.
            Logger.SessionLost();
            SetState(HostSessionState.Opening);
            return null;
        }

        return Publish(grant.Id, member, deadline);
    }

    /// <summary>
    /// Holds the session that has joined and makes it current.
    /// </summary>
    /// <param name="id">The identity of the session.</param>
    /// <param name="member">The member the node joined as.</param>
    /// <param name="deadline">The deadline that began before the session was opened.</param>
    /// <returns>The session.</returns>
    private HostSession Publish(SessionId id, Member member, HolderDeadline deadline)
    {
        var ended = new CancellationTokenSource(deadline.Remaining, Time);
        var session = new HostSession(id, member, ended.Token);
        TaskCompletionSource<HostSession> next;

        lock (Gate)
        {
            Session = session;
            EndedSource = ended;
            CurrentState = HostSessionState.Active;
            next = Next;
            Next = new TaskCompletionSource<HostSession>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        Logger.SessionOpened(Settings.NodeId);
        next.TrySetResult(session);
        return session;
    }

    /// <summary>
    /// Renews the session on each tick until it is lost or the host stops.
    /// </summary>
    /// <param name="session">The session to renew.</param>
    /// <param name="timer">The timer that ticks once for each renewal.</param>
    /// <param name="stopping">The token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when the session is lost.</returns>
    private async ValueTask HoldAsync(
        HostSession session,
        PeriodicTimer timer,
        CancellationToken stopping
    )
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stopping, session.Ended);

        while (true)
        {
            try
            {
                _ = await timer.WaitForNextTickAsync(wake.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                // The holder's deadline passed before a renewal.
                await LoseAsync(session, stopping).ConfigureAwait(false);
                return;
            }

            var deadline = HolderDeadline.Begin(Time, Settings.TimeToLive, Settings.DriftAllowance);
            KeepAliveResult result;

            try
            {
                result = await Sessions
                    .KeepAliveAsync(session.Id, null, stopping)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                Logger.KeepAliveFailed(exception);
                SetState(HostSessionState.Unreachable);
                continue;
            }

            if (!result.IsRenewed || !Extend(session, deadline))
            {
                await LoseAsync(session, stopping).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Extends the end of the held session to a renewed deadline.
    /// </summary>
    /// <param name="session">The session that was renewed.</param>
    /// <param name="deadline">The deadline that began before the renewal was sent.</param>
    /// <returns>True when the session was extended; false when it was already lost or its deadline has passed.</returns>
    private bool Extend(HostSession session, HolderDeadline deadline)
    {
        lock (Gate)
        {
            // Checked first, so that CancelAfter never cancels, and so never runs callbacks, under the gate.
            if (
                Session != session
                || deadline.HasPassed
                || EndedSource is not { IsCancellationRequested: false } ended
            )
            {
                return false;
            }

            ended.CancelAfter(deadline.Remaining);
            CurrentState = HostSessionState.Active;
            return true;
        }
    }

    /// <summary>
    /// Forgets a session that ended, ends its token and closes it.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    /// <param name="stopping">The token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when the session has been closed or the close has failed.</returns>
    private async ValueTask LoseAsync(HostSession session, CancellationToken stopping)
    {
        CancellationTokenSource? ended = null;

        lock (Gate)
        {
            if (Session == session)
            {
                ended = EndedSource;
                Session = null;
                EndedSource = null;
                CurrentState = HostSessionState.Opening;
            }
        }

        Logger.SessionLost();
        EndQuietly(ended);

        // Releases what is bound to the session when the provider still holds it.
        await CloseQuietlyAsync(session.Id, stopping).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes a session and logs a failure instead of throwing it.
    /// </summary>
    /// <param name="session">The session to close.</param>
    /// <param name="stopping">The token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when the close has finished.</returns>
    private async ValueTask CloseQuietlyAsync(SessionId session, CancellationToken stopping)
    {
        try
        {
            await Sessions.CloseAsync(session, null, stopping).ConfigureAwait(false);
        }
        catch (Exception exception) when (!stopping.IsCancellationRequested)
        {
            Logger.SessionCloseFailed(exception);
        }
    }

    /// <summary>
    /// Sets the state unless the host has stopped.
    /// </summary>
    /// <param name="state">The state to set.</param>
    private void SetState(HostSessionState state)
    {
        lock (Gate)
        {
            if (CurrentState != HostSessionState.Stopped)
            {
                CurrentState = state;
            }
        }
    }

    /// <summary>
    /// Cancels the token of a session and disposes its source, and logs a callback that threw instead of throwing it.
    /// </summary>
    /// <param name="ended">The source to cancel, or null for none.</param>
    private void EndQuietly(CancellationTokenSource? ended)
    {
        if (ended is null)
        {
            return;
        }

        try
        {
            ended.Cancel();
        }
        catch (AggregateException exception)
        {
            Logger.EndedCallbackFailed(exception);
        }
        finally
        {
            ended.Dispose();
        }
    }
}
