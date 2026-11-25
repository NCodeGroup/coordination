# 1018. A holder's local deadline starts before its request and ends an allowance for drift before the time to live

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

Every holder of a session, lease, semaphore slot, leadership or claim treats it as lost at a local deadline: the
duration, measured on the holder's own monotonic clock from before it sent the request, shortened by a margin for drift
([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md),
rule 6). The contracts return the time to live that a provider granted, but nothing measures the holder's side of it.

Only the holder knows when it sent the request. The provider sees the request later, and the response reaches the holder
later still, so a deadline measured from the response outlasts the grant. A renewal returns only whether the grant held,
so it carries no time that a provider could stamp.

## Decision

**A holder begins a `HolderDeadline` on its own clock before each request that grants or renews its ownership, and
treats the ownership as lost once it passes.**

1. **Begun by the holder.** `HolderDeadline` in `NCode.Coordination.Abstractions` is begun with the holder's
   `TimeProvider`, the time to live the request asks for, which is never longer than the one granted, and a drift
   allowance from zero up to but not including that time to live.
2. **Passes early.** It passes once `timeToLive - driftAllowance` has elapsed on the monotonic clock since it began, and
   it reports its remaining time and whether it has passed.
3. **Bound to a session.** A grant bound to a session lives as its session does, so its holder keeps the deadline it
   began before it opened the session or before its latest keep-alive.
4. **Shapes unchanged.** Grants and renewals keep their shapes, and providers stamp nothing.
5. **Fencing.** A pause can outlast the deadline, so a resource that takes writes from holders relies on the fencing
   token
   ([ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)),
   never on the deadline.

## Options considered

- **A deadline on each grant, stamped by the provider.** Rejected: a renewal returns only whether it held, so every
  grant and renewal would change shape, and a holder that begins before it calls is earlier than any stamp that a
  provider can give, so it is more conservative.
- **A drift rate instead of an allowance.** Rejected: the holder knows its tolerance as a duration, as a token validator
  knows its leeway.

## Consequences

- A holder that does not begin a deadline before its request has none.
- The recipes and the hosting begin a deadline for each holder they keep.
- The conformance kit cannot see a holder's clock, so the type's own tests prove it.

## References

- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)
- Gray, Cheriton, "Leases: An efficient fault-tolerant mechanism for distributed file cache consistency", SOSP 1989
- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006
- Kleppmann, "Designing Data-Intensive Applications", O'Reilly 2017, chapter 8 (unreliable clocks, process pauses)
