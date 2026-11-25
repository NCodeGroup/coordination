# 1014. Records carry a keyspace-wide revision, and watches and transactions are written in it

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

Records, their watches and their transactions need one notion of order that every provider can supply.

etcd gives every change a store-wide revision, keeps old revisions until compaction, starts a watch from a revision and
cancels one whose start was compacted. It runs a transaction as conditions on a key's version, creation revision,
modification revision or value, then one set of operations or another, and a key may be written only once in a
transaction (read 2026-11-25).

Consul gives each key a `ModifyIndex`, answers a blocking query with the latest state rather than each change, may reset
its index and keeps no history. It runs up to 64 operations as a transaction that rolls back on any failed check, with
no value comparison (read 2026-11-25).

## Decision

**A record carries a revision that grows with every write in its keyspace. Watches resume from a revision and converge
on the store's state, and a transaction is conditions plus two branches of writes, all in one keyspace.**

1. **Revisions.** A revision is a positive number that is greater for every later write in the same keyspace, not
   necessarily consecutive. A record is its key, an opaque value of at most 65,536 bytes and its revision.
2. **Lifetimes.** A record's lifetime is persistent, a time to live or a session. A compare-and-swap carries a lifetime,
   so sliding expiry is a write ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
   rule 7).
3. **Operations.**
   - Create if absent returns written with the record's revision, conflict, or session ended.
   - Read returns null when the record is absent.
   - Compare-and-swap on the revision returns written, conflict, not found, or session ended.
   - Compare-and-delete returns deleted, conflict or not found.
   - Take, an atomic read and delete, returns null when the record is absent.
   - List returns the records under a prefix, the prefix's own record included, in an order the provider chooses, at
     most a page's limit with an opaque continuation token, and the revision the page reflects.
4. **Watches.** A watch on a key or a prefix starts from an optional revision and streams written, deleted and resync
   events in revision order. The stream starts with a resync unless the provider keeps the history after the given
   revision, which it then replays, and it resyncs again whenever a change may have been missed. A key's intermediate
   values may be skipped, but after applying every event up to a revision, the subscriber's view equals the store at that
   revision. On a resync the subscriber rereads.
5. **Transactions.** A transaction has 0 to 64 conditions (absent, revision equals, value equals), 1 to 64 writes when
   they hold and 0 to 64 when they do not (put with a lifetime, delete), all in one keyspace and each key written at
   most once per branch. Its result says whether the conditions held, or that a write of the chosen branch
   binds a record to a session that has ended, when nothing is written, and the revision of the outcome. etcd's
   transactions map onto it directly. Consul's compare no values and have no else branch, so a Consul provider declares
   transactions unsupported with that reason.
6. **Partitions.** Two keys share a partition when they have the same partition depth `d` above 0 and equal first `d`
   segments, or are the same key. A provider that keeps `atomicity.within-partition` only refuses a transaction across
   partitions as `unsupported`.

## Options considered

- **Per-key versions.** Rejected: no keyspace order exists to resume a watch from.
- **A watch that delivers every intermediate value.** Rejected: Consul cannot, and convergence is what a cache needs.
- **Transactions without an else branch.** Rejected: the design asks for both branches, and etcd keeps them.
- **Listings sorted by key.** Rejected: every provider's key encoding would have to keep one order.

## Consequences

- A cache lists, then watches from the listing's revision.
- A Consul provider's watch resyncs on every start and index reset.

## References

- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- [ADR-1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)
- etcd v3.5, "etcd API" (<https://etcd.io/docs/v3.5/learning/api/>), read 2026-11-25
- Consul HTTP API, [KV](https://developer.hashicorp.com/consul/api-docs/kv),
  [transactions](https://developer.hashicorp.com/consul/api-docs/txn) and
  [blocking queries](https://developer.hashicorp.com/consul/api-docs/features/blocking), read 2026-11-25
