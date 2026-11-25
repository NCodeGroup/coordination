# 1007. The first stable release carries the whole model, proven on three providers and by its first consumer

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

The design's Q3 asks which capabilities beyond NCode.Identity's needs the first stable release carries. The release
matters because of what it freezes: before it, the project is pre-release and a contract changes in place
([ADR-0015](project-template/0015-a-pre-release-project-describes-the-product-as-it-is.md)); after it, every change to
a published contract needs a deprecation and a major version. What is cheap to change before the release is the shape
of the contracts, and what stays cheap after it is adding to them.

Two kinds of addition differ in cost after the release:

- **Additive by construction.** A new provider package, a vendor profile, a guarantee, a primitive name or an option is
  a new type or a new member of an open vocabulary
  ([ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md),
  [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)), and needs no
  change to what shipped.
- **Additive only if the model already fits it.** A primitive or recipe added later must fit the shape of the
  primitives it builds on: a read-write lock tests whether semaphores and fencing compose; partition ownership tests
  whether membership watches carry what an assignment needs; a transaction tests whether records' revisions and
  partitions are the right ones. A recipe that finds a primitive's shape wrong after the release finds it too late.

A contract is also only proven once a server with different constraints implements it. The in-process provider keeps
every promise trivially; a RESP server and a relational database differ in atomicity, expiry, change notification and
failover, so a contract that both implement is less likely to carry one server's assumptions.

## Decision

**The first stable release carries every primitive and recipe of the design, implemented by the in-process, RESP and
PostgreSQL providers and verified by the conformance kit, and is cut only after NCode.Identity runs on it. The etcd and
Consul providers, further vendor profiles and the native server follow as additive releases.**

1. **Every primitive and recipe.** The release carries all the design's primitives (sessions, membership, leases,
   semaphores, elections, claims, records with prefix listing and watches, transactions, occurrences, sets, counters,
   sequences and notifications) and all its recipes (locks, read-write locks, barriers, singletons, partition
   ownership, single-flight, rate limiters and cache invalidation), whether or not NCode.Identity uses them.
2. **Three providers.** The in-process provider implements every primitive. The RESP provider, with its Valkey and
   Garnet profiles, and the PostgreSQL provider each implement every primitive their server can keep honestly and
   declare the rest unsupported with the reason. Every primitive has at least one of them that passes the conformance
   kit for it against a real server; one that has none is held back from the release.
3. **The first consumer before the freeze.** The first stable release is cut after the first-consumer adoption phase:
   NCode.Identity runs on prerelease packages, so the contracts its needs depend on are confirmed while changing them
   is still free. How it consumes them is the design's Q1, still open.
4. **Later providers are checked for fit, not shipped.** While the contracts are designed, each is checked against the
   public API documentation of etcd v3 and Consul (sessions, leases, watches and transactions), so their providers add
   packages and declarations without changing a shipped contract.
5. **What follows the release.** The etcd and Consul providers, vendor profiles beyond the RESP provider's two, a
   primitive or recipe the design does not list, and the native server ship in minor releases, as additions.

## Options considered

- **The first consumer's needs alone.** Rejected: the contracts would be shaped by one consumer and frozen before the
  primitives that test them exist, contrary to
  [ADR-1000](1000-ncode-coordination-is-a-general-coordination-library-for-the-nodes-of-a-cluster.md), and the
  services that need semaphores, barriers or partition ownership would wait for a major version that fixes the shapes
  those recipes expose.
- **Every provider the design names, etcd and Consul included.** Rejected: those providers add packages and
  declarations, which are additive by construction, so holding the release for them delays every consumer without
  making a shipped contract safer than checking each contract against their documentation does.
- **The model with the in-process provider alone.** Rejected: no contract would have met a server's atomicity, expiry
  or failover constraints before it froze.

## Consequences

- The Model phase builds every primitive and recipe, so it is several plans long, and the RESP and PostgreSQL phases
  implement all of them.
- The first stable release waits for the first-consumer adoption phase, which lands in another repository.
- A primitive that no networked provider can keep is visible before the release, as a gap in the conformance results,
  not as a defect after it.
- A contract found wrong by the second provider or by the first consumer is changed in place, with no deprecation.

## References

- [Design: Rollout and Phases](../designs/coordination.md#rollout-and-phases)
- [ADR-1000](1000-ncode-coordination-is-a-general-coordination-library-for-the-nodes-of-a-cluster.md)
- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)
- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
- [ADR-0015](project-template/0015-a-pre-release-project-describes-the-product-as-it-is.md)
