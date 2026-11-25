# 1008. Expiry is a duration on the provider's declared clock, and an instant becomes one at the caller with an allowance for skew

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

The design's Q4 asks whether expiry is evaluated on the server's clock for every provider that can, or on the caller's
where a feature needs an absolute instant. [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
already states that a caller gives expiry as a duration and that a lease holder assumes its lease lost early; this
record settles the rest.

Three kinds of clock are involved: the server's, the calling node's, and every other node's that reads the same state.
They disagree by an unknown skew and drift apart at a bounded rate. A duration is measured on one clock, so skew
between two clocks does not change it; an instant is read on one clock and compared on another, so skew between them
moves it. The direction matters: a value that guards against replay is safe when it lives too long and unsafe when it
expires early, and a lease is safe when its holder gives it up early and unsafe when it holds on late.

Some features' lifetimes are naturally instants. A DPoP proof's or a client assertion's `jti` must be remembered until
the proof or assertion stops being acceptable on any node, an instant derived from its `iat` or `exp`
([RFC 9449](https://www.rfc-editor.org/rfc/rfc9449), [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519) §4.1.4, which
allows a validator some leeway for clock skew). A per-interval claim and a daily quota have window boundaries on the
calendar. Others are naturally durations: a session's time to live, a sliding session expiry, a lease.

Servers measure expiry differently: some in milliseconds, some in whole seconds, and some have no expiry of their own,
so a client must evaluate it.

## Decision

**Every expiry a contract takes is a duration, measured on the clock the provider declares as its expiry authority,
which is the server's wherever the server can measure it. A feature whose lifetime is an instant converts it to a
duration on its own clock, adding an allowance for the skew between the nodes that rely on it. Holders of time-bound
ownership give it up at a conservative local deadline and rely on fencing for the rest.**

1. **Durations only.** No operation sends an absolute instant to a server. A provider applies a duration from the
   moment its server applies the write, so the calling node's clock and the server's never need to agree.
2. **The server's clock wherever it can.** A provider measures expiry on its server's clock when the server can, and
   declares the `expiry.server-clock` guarantee; a provider whose server cannot measures it on the caller's clock and
   declares `expiry.caller-clock`. A feature that requires the server's clock refuses the second at startup. The
   in-process provider is its own server and measures on the process's `TimeProvider`.
3. **Rounded up, never down.** A provider whose server measures in coarser units than the duration rounds it up.
4. **Instants are converted by the caller, with an allowance.** A feature whose lifetime ends at an instant computes the
   duration as that instant minus its own clock's current time, plus an allowance at least as large as the clock skew
   it tolerates between the nodes that read the state, so the state outlives the last node that could still act on it.
   An allowance only lengthens a lifetime. When the instant is already past by more than the allowance, every node
   already refuses what the state would guard, and there is nothing to record.
5. **Windows are named on the caller's clock.** A claim's or counter's window is named by its start, computed on the
   caller's clock and aligned to the window's length from the Unix epoch, and the window's state lives until the window
   ends, plus the allowance of rule 4. Skew changes when a node moves to the next window, never lets two holders claim
   the same window.
6. **Holders keep a conservative local deadline.** A holder of a session, lease, semaphore slot, election term or claim
   treats it as lost at a local deadline: the duration, measured on its own monotonic clock from before it sent the
   request, shortened by a margin for clock drift. A pause can outlast the deadline, so a resource that takes writes
   from holders relies on the fencing token of
   [ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md),
   never on the deadline.
7. **Sliding expiry is a write.** Extending a lifetime is a write that carries a new duration, measured from when the
   server applies it.

## Options considered

- **Absolute instants, compared on the server's clock.** Rejected: skew between the caller and the server moves every
  expiry, earlier when the caller's clock runs behind, which lets a replayed value through, and a provider cannot round
  an instant on a clock it cannot see.
- **Durations on the server's clock, with no rule for instants.** Rejected: each feature would convert its instants in
  its own way, without an allowance, which is how lifetimes end up tuned per feature and correct only under conditions
  nothing states.
- **The caller's clock wherever a feature needs an instant.** Rejected: it makes each node's clock an authority over
  state other nodes rely on, so one fast clock expires everyone's state early.

## Consequences

- A feature that converts an instant states its skew allowance, as a token validator already states its leeway.
- A provider whose server has no expiry can still carry primitives, declaring the caller's clock, and features that
  need the server's clock refuse it.
- Expiry in tests runs on a `TimeProvider` the test controls, and the conformance kit checks that a provider measures a
  duration from the write and never shortens it.
- A holder's view of its lease is always conservative, never authoritative.

## References

- Gray, Cheriton, "Leases: An efficient fault-tolerant mechanism for distributed file cache consistency", SOSP 1989
  (bounded clock drift instead of synchronized clocks)
- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006 (a client's conservative local
  lease timeout)
- Kleppmann, "Designing Data-Intensive Applications", O'Reilly 2017, chapter 8 (unreliable clocks, process pauses)
- [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519), JSON Web Token, §4.1.4
- [RFC 9449](https://www.rfc-editor.org/rfc/rfc9449), OAuth 2.0 Demonstrating Proof of Possession
- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)
