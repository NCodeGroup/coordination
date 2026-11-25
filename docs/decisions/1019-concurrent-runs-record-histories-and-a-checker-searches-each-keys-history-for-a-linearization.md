# 1019. Concurrent runs record histories, and a checker searches each key's history for a linearization

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

The contract tests of
[ADR-1017](1017-the-conformance-kit-is-a-set-of-xunit-contract-classes-a-providers-tests-derive-from.md) call one
operation at a time. They cannot show that concurrent calls appear to take effect one at a time, or that a subscriber
learns it may have missed a notification.

[ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md) asks
for checked histories: clients record every call and return, and a checker decides whether the history keeps each
declared guarantee. A call that fails says whether it may have applied
([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)),
so a history holds calls whose effect is unknown.

The checker is written from papers only, as [ORIGINALITY.md](../../ORIGINALITY.md) requires: no other project's source
is opened, ported or translated.

## Decision

**Each primitive's suite runs concurrent clients against the provider, records every call and return, and checks the
history against the primitive's sequential specification for linearizability, and against its declared delivery model
for notifications and record watches.**

1. **Recording.** Every call and every return is stamped from one counter that is incremented atomically: the history's
   monotonic clock, which never ties as `Stopwatch` ticks can. A call that fails with a `CoordinationException` whose
   `OutcomeUnknown` is false is `Failed` and is left out of checking. A call that fails with one whose `OutcomeUnknown`
   is true, is cancelled by something other than the run's token, or gets no answer within 10 seconds is
   `Indeterminate`: it returns at `long.MaxValue`, as Herlihy and Wing's pending call does. Any other exception fails
   the test.
2. **Sequential specifications.** Each primitive has a model: a state, and a `TryApply` that returns whether an
   operation with an output is allowed from that state and what the state becomes. A model never rejects what a correct
   provider can return: a token, revision or term that an indeterminate call never returned is unknown, so it is
   accepted and learned from the next result that shows it; an indeterminate semaphore acquire is counted as unknown and
   not given a slot. Waits and membership and election watches are not modelled, because they carry no guarantee a
   history can check. An operation that spans keys, a listing of records or a counter batch over two keys, has a model
   whose state holds both keys' states: a call on one key applies that key's model; a listing shows each record it
   lists as a read of its key would, and every present record when it is the last page, at a keyspace revision no older
   than any revision known for either key, since a page is consistent on its own; and a batch applies its increments in
   order and stops after the first whose count exceeds its limit.
3. **The checker.** The checker is Wing and Gong's search over the calls that no other call returned before, with
   Lowe's cache of configurations that were already searched. It splits the history by partition, since
   linearizability is local (Herlihy and Wing): a run records each call in its key's partition, or every call in one
   partition when its operations span keys, as a listing, a multi-key counter batch or a transaction does. A call on
   `"*"`, a clock advance made while nothing is in flight, joins every key. The search gives up after 1,000,000
   configurations and fails the test, so a run never hangs.
4. **The report.** The checker reports the first failing key in ordinal order, cut to its shortest failing prefix by
   binary search, since every prefix of a linearizable history is linearizable. The report describes each call with its
   client, input, output and stamps.
5. **Delivery.** A subscriber's messages keep what the provider declares. With `delivery.at-most-once`, each message was
   published by a call that began before it arrived, each number is carried by one payload and each payload has one
   number, and numbers rise. With `delivery.at-most-once-with-gap-detection`, a number that skips ahead needs a resync
   first, a resync carries the last number delivered, and the last message or a resync after it arrives. A record watch
   keeps the same rules in revisions: a change is a write or deletion some call made, revisions rise, and with gap
   detection each key's closing write or a later resync arrives.
6. **Runs.** A run has 3 clients of 12 steps over 2 keys, each call chosen from the client and the step, with no
   randomness, and lifetimes of 30 seconds so nothing expires. Only the paused-holder run of leases moves the clock,
   and it records the move as a `ClockAdvance`. A run checks only the guarantees the provider declares, closed under
   `GuaranteeDefinition.Implies`: linearizability for `consistency.linearizable-within-process`, as every client shares
   the test's process; delivery for `delivery.at-most-once`; gap rules for `delivery.at-most-once-with-gap-detection`.
   Otherwise the run skips with the reason.
7. **Faults.** The paused holder is driven by the kit and needs the target's `AdvanceClock`. A run of records or
   notifications that drops connections calls the target's `DropConnections` once half the steps are done, and skips
   when the target has none. A client that finds half the steps done waits until the fault has begun, so the calls of
   the second half meet it however late it is scheduled. A subscription that fails with a `CoordinationException` ends
   that subscriber's reading. Restart and failover come with the providers.
8. **Surface.** The checker, the models and the runs are internal to `NCode.Coordination.Testing`, which grants
   `InternalsVisibleTo` to its own test project. The public surface adds only `ConformanceTarget.DropConnections` and
   the suites' `ConcurrentRun_*` tests.

## Options considered

- **Port an existing checker.** Rejected: it would be a derivative work, which ORIGINALITY.md forbids.
- **Lowe's just-in-time linearization.** Rejected: it is more involved and not needed at the size of a run.
- **Seeded random scripts.** Rejected: the interleaving of clients varies from run to run anyway, so a seed does not
  reproduce a failure.
- **Expiry in every model.** Rejected: only the paused-holder run moves the clock, so the other models need no time.
- **A public checker.** Rejected: an internal one can be widened later without a break.

## Consequences

- Providers get the concurrent runs through the suites they already derive.
- The runs that drop connections run in this repository's CI against a test decorator of the in-process provider, whose
  drop fails the next three calls with an unknown outcome, one of them after it applied, and gives each subscription a
  resync in place of its next message; against the in-process provider alone they skip, as it has no connection.
- A violation that spans keys is caught only by the runs that keep their keys in one partition: listings, multi-key
  counter batches and transactions.
- Durability and expiry authority wait for restart seams and a server clock.

## References

- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
- [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
- [ADR-1017](1017-the-conformance-kit-is-a-set-of-xunit-contract-classes-a-providers-tests-derive-from.md)
- [ORIGINALITY.md](../../ORIGINALITY.md)
- Herlihy, Wing, "Linearizability: A correctness condition for concurrent objects", ACM TOPLAS 12(3), 1990
- Wing, Gong, "Testing and verifying concurrent objects", Journal of Parallel and Distributed Computing 17(1-2), 1993
- Lowe, "Testing for linearizability", Concurrency and Computation: Practice and Experience 29(4), 2017
