# 1000. NCode.Coordination is a general coordination library for the nodes of a cluster

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

A service that runs as several nodes needs agreement on liveness, ownership, one-time events, change and usage. Each
service that builds this agreement for itself builds it from a cache's commands, states its guarantees nowhere, and
tests it against a mock. NCode.Identity is such a service: its shared-state ports cover the needs it met as they came,
and its features build the rest (per-interval passes, a refresh lease, missed-broadcast recovery) on ports not shaped
for them.

The established name of this category is a coordination service: a lock service, a coordination kernel, a key-value
store for a distributed system's most critical data. The category's concepts — sessions, leases, fencing, revisions,
watches, membership — are published and stable.

## Decision

**NCode.Coordination is a general-purpose .NET library for coordinating the nodes of a cluster, in a repository and
release cycle of its own.**

1. **Its name says what it does.** `NCode.Coordination` names the category, and each package names one part of it
   (`NCode.Coordination.Abstractions`, `NCode.Coordination.Resp`, and so on).
2. **Its first consumer is a floor, not a ceiling.** NCode.Identity's needs are the minimum the first stable release
   meets. The model also covers the coordination problems services commonly meet — semaphores, read-write locks,
   barriers, partition ownership, a service registry, transactions, sequences — whether or not Identity needs them.
3. **It coordinates; it does not store or broker.** It is not a database, a cache, a message broker, an actor runtime
   or a storage server. Its state is small, often short-lived, and never the only copy of business data.
4. **It is independent of its first consumer.** No package references NCode.Identity, and no concept in it is named for
   identity.

## Options considered

- **Grow `NCode.Identity.SharedState` in place.** Rejected: the ports would stay shaped by one consumer, versioned with
  it and named for it, and a service outside Identity could not take them without the family's composition.
- **Adopt one coordination server's client directly.** Rejected: a service would depend on that server's semantics and
  operations, and could not choose a server it already runs.
- **A name from the category's vocabulary alone, such as `NCode.Cluster` or `NCode.Consensus`.** Rejected: a cluster
  library suggests hosting and actors; a consensus library suggests a Raft implementation, which most providers are not.

## Consequences

- Identity depends on NCode.Coordination as it depends on any library, and its `SharedState` packages are retired once
  it does.
- The product carries capabilities no consumer uses yet, so its conformance kit, not a consumer's tests, is what proves
  them.
- Two repositories change together while Identity adopts it, which needs a way to consume unreleased packages.

## References

- [Design](../designs/coordination.md)
- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006
- Hunt, Konar, Junqueira, Reed, "ZooKeeper: Wait-free coordination for Internet-scale systems", USENIX ATC 2010
