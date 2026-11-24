# 1002. Primitives declare guarantees, and the composition refuses a requirement no provider meets

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

The same operation keeps different promises on different servers. A create-if-absent is linearizable on a single
primary until it fails over, when an acknowledged write may be lost; on a consensus store it survives failover; on a
multi-threaded server it may be atomic only in a mode the server must be started with. A replay check tolerates a lost
write for the lifetime of the token it guards; a lease that guards a one-time presentation does not. A library that
hides these differences behind one interface lets a feature run on a provider that cannot keep its promise, and the
first sign is a production incident.

## Decision

**Every primitive's operations carry named guarantees; each provider declares the guarantees it keeps per primitive;
each feature declares the guarantees it requires; and the composition fails at startup when a requirement is bound to
a provider that does not declare it.**

1. **The guarantee vocabulary.** Consistency (linearizable across failover; linearizable until failover; local to one
   process), durability (volatile; asynchronously replicated; durable), expiry authority (server clock; caller clock)
   and notification delivery (at most once with gap detection; at most once). The vocabulary is open: a provider or a
   consumer names another guarantee, with the guarantees it implies.
2. **Binding.** A host binds a provider to all primitives, to some, or to a keyspace, so occurrences and leases can
   live on different servers.
3. **Unsupported is explicit.** A provider that cannot keep a primitive honestly declares it unsupported; it never
   emulates one with weaker guarantees than it declares.
4. **Expiry is a duration.** A caller states how long a value lives, and the provider's expiry authority applies it; a
   lease holder assumes its lease lost before the duration ends on its own clock, by a margin for clock drift.

## Options considered

- **One interface, best effort.** Rejected: the guarantee a feature relies on is invisible, which is how a lease built
  on an asynchronously replicated cache becomes two holders.
- **One mandatory guarantee level for every provider.** Rejected: it either excludes the servers most services run or
  promises what they cannot keep.

## Consequences

- A host learns at startup, not in production, that a provider cannot carry a feature.
- Providers carry a guarantee table, and the conformance kit tests each entry.

## References

- [Design: Guarantees](../designs/coordination.md#guarantees)
- Herlihy, Wing, "Linearizability: A correctness condition for concurrent objects", TOPLAS 1990
- Gray, Cheriton, "Leases: An efficient fault-tolerant mechanism for distributed file cache consistency", SOSP 1989
