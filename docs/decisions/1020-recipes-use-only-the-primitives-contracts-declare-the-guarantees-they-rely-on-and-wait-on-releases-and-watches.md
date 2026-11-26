# 1020. Recipes use only the primitives' contracts, declare the guarantees they rely on, and wait on releases and watches

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group
- **Amends:** 1016

## Context

The design lists recipes over the primitives: locks, read-write locks, barriers, singletons, partition ownership,
single-flight, rate limiters and cache invalidation ([the design](../designs/coordination.md#primitives)). A recipe
runs on every provider that keeps what it needs only when it calls nothing but the primitives' contracts, and the
composition refuses a binding that cannot keep a feature's needs only when the feature declares them
([ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md),
[ADR-1016](1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md)).

Acquisition never blocks, so waiting and fairness belong to the recipes
([ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)).
A lease's wait completes once the resource has no holder; a semaphore's completes once any slot is free, and a session
that acquires a semaphore again receives the slot it already holds. A record watch resumes from a revision and resyncs
whenever a change may have been missed
([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)).

`System.Threading.RateLimiting` is the .NET runtime's rate limiting package, under the MIT license. Its `RateLimiter`
has a synchronous `AttemptAcquire`, which never queues, and an asynchronous `AcquireAsync`, which may queue; ASP.NET
Core's rate limiting middleware and `PartitionedRateLimiter` take any `RateLimiter`, and the middleware queues a
request when its limiter does (read 2026-11-25). Version 11.0.0, published 2026-11-10, targets `net10.0` with no
dependencies (read 2026-11-25).

## Decision

**Each recipe is an interface in `NCode.Coordination` whose implementation calls only the primitives' services; it
declares the primitives and guarantees it relies on as a `Recipe` that a host requires with `RequireRecipe`, and it
waits on releases and watches, never on a timer.**

1. **Contracts only.** A recipe's implementation takes the routed primitive services from dependency injection, so
   every operation it makes goes to the bound provider and is traced and measured as any other. No recipe reaches a
   provider directly, keeps coordination state in the process, or adds an instrument of its own.
2. **Declared needs.** A `Recipe` lists the primitives it uses and the guarantees it relies on, and
   `CoordinationRecipes` holds the built-in ones. `RequireRecipe` adds one feature requirement per need, so a host
   whose binding cannot keep them fails to start. Recipes need `consistency.linearizable-until-failover` on the
   leases, records, elections and counters they use, and `delivery.at-most-once-with-gap-detection` on the record
   watches and notifications they use; partition ownership needs membership with no particular guarantee, since its
   assignment is advisory.
3. **Keys.** A recipe works under the caller's key and adds at most one segment for its own state, so the caller's key
   has at most 15 segments, and the keys under it belong to the recipe.
4. **Waiting.** A recipe waits on a release or a watch, without fairness, until its outcome, a cancellation or a
   failure. It opens one watch per wait from the revision of the listing before it, and a resync rereads the state
   while the stream continues.
5. **Outcomes.** A wait returns the grant, or nothing when the session in its terms has ended. Failures propagate as
   the primitives throw them
   ([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)),
   and state a recipe took for itself before a failure or a cancellation is given back. A failure to give it back is
   not thrown, since the state ends with its time to live or its session, so the outcome or the failure before it
   stands. A recipe that returns a grant leaves the holder's deadline to its caller, and a singleton relies on its
   session, whose holder keeps the deadline
   ([ADR-1018](1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md)
   rule 3).
6. **Locks.** A lock retries the lease each time the resource has no holder, and its grant carries the lease's fencing
   token.
7. **Read-write locks.** A writer holds the lease `<resource>/writer`, then waits until no reader's record is under
   the resource. A reader holds that lease as a probe for at most 30 seconds while it writes its record
   `<resource>/<probe's fencing token>`, bound to its session or time to live, and retries when the probe was lost
   before its release. A read grant carries the probe's fencing token, which orders it among the write grants.
8. **Barriers.** Each participant writes a record under the barrier's key, and a party counts once it is seen
   arriving, even when its record later ends. A barrier serves 1 to 1,000 parties, once per key.
9. **Singletons.** A candidate leads the role with its session and runs the work with a token that is cancelled when
   the role shows another term or none. Work that completes resigns; work stopped by a lost leadership campaigns
   again; an ended session ends the run. A former leader's work can still be finishing when the next leader starts, so
   a resource checks the term.
10. **Partition ownership.** Each partition goes to the live member with the highest rendezvous score: the first 8
    bytes, big-endian, of SHA-256 over the member's node id in UTF-8 and the partition as a 4-byte big-endian integer,
    ties going to the ordinally smaller node id. A partition moves only when its owner leaves or a joining member
    outscores it.
11. **Single-flight.** A caller that finds no value takes the lease `<key>/fetch` for the fetch's time to live, fetches
    and writes the value for its own time to live, then releases the lease; the others wait for its release and read
    the value. A failed fetch releases the lease, so a waiting caller fetches next.
12. **Rate limiters.** The limiters are `RateLimiter` instances a factory creates per key. A fixed window counts in
    one counter per window aligned to the Unix epoch, and an attempt over the limit still counts
    ([ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)).
    A sliding window splits its window into 1 to 16 segments, each a counter, and adds the earlier segments' counts to
    the current one's. A token bucket keeps its tokens and its last replenishment in one record, changed by
    compare-and-swap and replenished on the caller's clock; no record is a full bucket. Windows and periods are at
    most 180 days, and state lives until it stops counting plus one minute for clock skew. `AttemptAcquire` cannot
    reach the provider without blocking, so it answers not acquired, and `AcquireAsync` never queues and gives a
    retry-after when it denies. A provider failure throws, since a limiter cannot tell whether it guards capacity or
    abuse.
13. **Cache invalidation.** An invalidation is a notification on a topic that names the entry. The subscriber
    invalidates that entry, clears the cache on a resync, and clears it again whenever it stops listening.
14. **Dependency.** `NCode.Coordination` also depends on `System.Threading.RateLimiting`, which amends rule 11 of
    ADR-1016, so its limiters plug into ASP.NET Core's rate limiting middleware and `PartitionedRateLimiter`.

## Options considered

- **Recipe contracts in the abstractions.** Rejected: the limiters derive from a type of a package, and the
  abstractions use the BCL only.
- **A read-write lock over semaphore slots.** Rejected: a semaphore's wait completes when any slot is free, so a
  writer cannot wait for every reader to leave, and a session that acquires again gets the slot it holds, so a writer
  cannot take every slot.
- **A read-write lock over a transaction.** Rejected: it would require transactions, which a provider may declare
  unsupported.
- **Waiting by polling.** Rejected: it adds load and latency, where releases and watches already signal.
- **Fair, queued locks.** Rejected: they need ordering state on the server; fencing tokens, not fairness, keep a
  resource safe.
- **Consistent hashing on a ring.** Rejected: it needs virtual nodes to balance, where rendezvous hashing balances
  with the same minimal movement.
- **A synchronous `AttemptAcquire` that waits for the provider.** Rejected: blocking on a task is banned.
- **Limiters that fail open.** Rejected: an abuse guard must not open when its store is unreachable.

## Consequences

- A host requires each recipe it uses with `RequireRecipe`, and a single-node host also calls `UseSingleNode()`.
- Locks and singletons promise no fairness, and a resource checks fencing tokens and terms.
- A rate limiter's windows and a token bucket's refill depend on the callers' clocks agreeing within the window.
- Recipes are measured as the primitive operations they make.

## References

- [The design](../designs/coordination.md), its Primitives
- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md),
  [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1016](1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md),
  [ADR-1018](1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md)
- Thaler, Ravishankar, "Using name-based mappings to increase hit rates", IEEE/ACM Transactions on Networking, 1998
- Wikipedia, "Rendezvous hashing" (<https://en.wikipedia.org/wiki/Rendezvous_hashing>), read 2026-11-25
- Microsoft Learn, `RateLimiter`, `RateLimitLease` and `RateLimitPartition`
  (<https://learn.microsoft.com/dotnet/api/system.threading.ratelimiting.ratelimiter>), read 2026-11-25
- Microsoft Learn, "Rate limiting middleware in ASP.NET Core"
  (<https://learn.microsoft.com/aspnet/core/performance/rate-limit>), read 2026-11-25
- Conroy, "Announcing Rate Limiting for .NET", .NET Blog, 2022
  (<https://devblogs.microsoft.com/dotnet/announcing-rate-limiting-for-dotnet/>), read 2026-11-25
- [Provenance log](../provenance.md)
