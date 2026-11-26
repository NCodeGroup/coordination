# 1021. A host holds one session for its lifetime, joins its membership with it, and closes it as it stops

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

A node keeps a session alive while it runs, and a graceful shutdown closes it, which releases every lease and
membership entry bound to it at once
([ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)
rule 1). Nothing yet does that
for a host: its features would each have to open, renew and close a session of their own.

The composition's requirement check and the provider probes run in a lifecycle service's starting step, and the start
fails when either does
([ADR-1016](1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md)
rules 6 and 7). A holder begins its deadline before its request and lets it pass an allowance for drift before the
time to live, and the design says the hosting begins one
([ADR-1018](1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md)).

The project template asks for configuration that binds strictly and is validated at startup
([project-template ADR-0106](project-template/0106-configuration-binds-strictly-and-is-validated-at-startup.md)),
for readiness checks that look at dependencies while liveness never does
([project-template ADR-0128](project-template/0128-liveness-never-checks-dependencies-readiness-does.md)), and for an
implementation package that references the abstractions only
([project-template ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)).

The generic host runs every service's `StartingAsync`, then every `StartAsync`, and every `StartedAsync`; on stop it
runs every `StoppingAsync`, then every `StopAsync`, then every `StoppedAsync`, with the stop token cancelled once the
shutdown timeout passes (read 2026-11-25).

## Decision

**A new package, `NCode.Coordination.Hosting`, opens one session as the host starts, joins its cluster's membership
group with it, renews it on its own clock, replaces it as soon as it loses it, and closes it in `StoppedAsync`, once
every other hosted service has stopped.**

1. **Package.** `NCode.Coordination.Hosting` references only `NCode.Coordination.Abstractions` among the product's
   projects, plus the hosting abstractions, options configuration extensions, logging abstractions and health checks
   packages. The host's composition root calls both `AddCoordination` and `AddCoordinationHosting`. The hosting adds no
   metric or span, since the routed primitive services already trace every session and membership call.
2. **Options.** `CoordinationHostOptions` is bound from the section `Coordination:Host`, with unknown keys an error, or
   set in code. It names the cluster and the keyspace segments under it, the membership group (`members` by default),
   the node id, the address, the node's metadata, the session's time to live (30 seconds), the keep-alive interval
   (10 seconds), the drift allowance (2 seconds), the retry delay (2 seconds) and the longest retry delay (30 seconds).
3. **One rule set.** The options are validated by the contracts' own constructors, so they obey exactly the rules the
   contracts enforce, and each invalid property adds one failure that names it. The keep-alive interval is more than
   zero, at most one day, and shorter than the time to live less the drift allowance; the retry delay is more than zero
   and at most one day, and the longest retry delay is at least the retry delay and at most one day. The same rules run
   when the options are validated on start, which fails the host's start with every failure, and when the session
   service is created.
4. **Opening.** The session service is a hosted lifecycle service. Its start opens the session on the starting thread
   until its first wait that does not complete at once, and never blocks the host's start: a host answers liveness
   probes while it is not ready, and a node restarted within its old session's time to live finds its node id taken
   until that session expires. The holder's deadline begins before the open request.
5. **Joining.** The host joins the group with a member made of the node id, the address and the metadata, and
   publishes the session only once it has joined, so the member of every published session is in the group. A failed
   open or join is logged and tried again after a wait, and the session just opened is closed quietly. When the node id
   is taken by another live session, the host logs it, closes the session and tries again after a wait, as it does when
   the session ended before it joined. The first wait is the retry delay, each further failure in a row doubles it up to
   the longest retry delay, and each wait is shortened at random by up to half, so a cluster's nodes, which lose their
   provider together, spread their attempts instead of retrying in step. A session that joins starts the waits over.
6. **Renewing.** The host renews the session every keep-alive interval, which starts when the session's open begins,
   and each renewal begins a holder's deadline. A renewal that fails to reach the provider is logged and leaves the
   session held; the next interval tries again.
7. **Ending.** The session's `Ended` token is cancelled when the holder's deadline passes without a renewal, when a
   renewal finds the session ended, and as the host stops. A session lost this way is closed quietly, in case the
   provider still holds it, and a new one opens at once.
8. **Features.** `IHostSessionService` gives `State`, the `Current` session (none unless one is held and its `Ended` is
   not cancelled) and `WaitForSessionAsync`, which returns the current session or the next one the host opens and
   throws once the host has stopped. A `HostSession` carries the session's id, its member and `Ended`. A feature binds
   its leases to the session's id and stops using them when `Ended` is cancelled; a callback registered on `Ended` must
   not throw.
9. **Closing.** The host closes the session in `StoppedAsync`, not `StopAsync`, because other services may still hold
   leases bound to it until they have stopped. It closes with the host's stop token, so the close ends when the
   shutdown timeout passes. A failed or timed-out close is logged, and the session ends when its time to live passes.
10. **Requirement.** `RequireHostSession()` and `RequireHostSession(Keyspace)` on `ICoordinationBuilder` require the
    session primitive with the guarantee `consistency.linearizable-until-failover`, since every node must agree whether
    a session is live, and the membership primitive with no particular guarantee. A host whose binding cannot keep them
    fails its start, as any unmet requirement does.
11. **Health.** One readiness check, `AddCoordinationCheck()`, named `coordination` and tagged `ready`, reads the
    session service's state and makes no call of its own, since the renewals are the probe. It is healthy while the
    host holds its session and the provider's last call succeeded, and otherwise reports the registration's failure
    status. Liveness checks no dependency.
12. **Logging.** The hosting's event ids are 2000 to 2999, leaving 1000 to 1999 to `NCode.Coordination`. No message
    carries a keyspace, a key or a segment, since they can name a tenant; the node id is the operator's.
13. **A faulted loop.** An exception that no call the loop guards expects is a defect, so the loop logs it as an error,
    the state becomes `Unreachable` and the loop ends. The host opens no further session until it restarts, the health
    check reports it not ready, and the stop still ends the token of the session it holds and closes it.

## Options considered

- **Blocking the host's start until the session opens.** Rejected: a host must answer liveness probes while it is not
  ready, and a node restarted within its old session's time to live would wait out that session before it started.
- **Closing the session in `StopAsync`.** Rejected: other hosted services may still hold leases bound to the session
  while they stop, and `StoppedAsync` runs after every one of them has stopped.
- **Leaving the group before the close.** Rejected: the close ends the membership entry at once, so a leave adds a
  second call that gains nothing.
- **Detecting a lost session with `ISessionService.WaitForEndAsync`.** Rejected: a renewal's result and the holder's
  deadline suffice, without a second operation for every session.
- **A health check that calls the provider.** Rejected: the renewals already call it every interval, and a second probe
  adds load without telling more.
- **A fixed retry delay.** Rejected: after a provider outage every node of a cluster would try again in step, and the
  recovering server would take each wave of attempts at once.
- **A wait anywhere from zero up to the doubled delay.** Rejected: a node could try again at once after a failure;
  keeping half of each wait fixed bounds how soon it tries again.
- **Retrying a faulted loop.** Rejected: an exception no guarded call expects is a defect, and a loop that retried it
  would also have to end the session it holds.

## Consequences

- A host restarted within its old session's time to live is not ready until that session expires, because its node id
  is taken until then, though it stays live.
- Features bind leases to `Current.Id` and stop using them when `Ended` is cancelled, and the session service never
  gives a session whose `Ended` is cancelled.
- A host whose binding cannot keep a linearizable session or a membership fails its start when it calls
  `RequireHostSession`, before the session service runs.
- A provider outage makes every node not ready, through the health check, without restarting any of them; each node
  recovers on its own when the provider does.
- A failed or timed-out close leaves the session to expire, so its leases and membership entry end when its time to
  live passes rather than at once.
- A package that needs the host's session takes `IHostSessionService` and has no session lifecycle code of its own.

## References

- [ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)
- [ADR-1016](1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md)
- [ADR-1018](1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md)
- [ADR-0106](project-template/0106-configuration-binds-strictly-and-is-validated-at-startup.md)
- [ADR-0128](project-template/0128-liveness-never-checks-dependencies-readiness-does.md)
- [ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)
- The Microsoft Learn pages and nuget.org entries listed in the [provenance log](../provenance.md)
