# 1024. The RESP provider keeps each keyspace in one cluster slot, with its revision beside it

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

A RESP server may run standalone, as a primary with replicas, or as a cluster that splits its data across shards by
hash slot. A host's Valkey may run in any of these modes, and so may Garnet. The RESP provider has to say which slot
each of its keys lands in, because:

- **F1. One revision per keyspace.** A record's revision is greater for every later write in its keyspace, and watches
  and transactions are written in it
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md) rule 1).
  On a RESP server that is a counter that every write in the keyspace increments in the same atomic step.
- **F2. One slot per operation.** Every multi-step operation is one Lua script, and every key it touches must lie in one
  slot ([ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
  rule 3). A record write touches the record and the revision counter, a session-bound write touches the session too,
  and a transaction touches up to 64 records of one keyspace.
- **F3. Listing and watching without a cluster-wide scan.** A list returns the records under a prefix, and a watch
  follows a key or prefix in revision order (ADR-1014 rules 3 and 4).
- **F4. Spreading load.** A cluster spreads work across shards only when keys spread across slots.
- **F5. Partitions.** A key may name a partition depth, and a provider that keeps atomicity within a partition only
  refuses a transaction across partitions (ADR-1014 rule 6). The contracts declare `atomicity.within-partition` and
  `atomicity.across-partitions`.

How a cluster places keys (read 2026-11-25; sources under References): a key's slot is the CRC16 of the key modulo
16,384, unless the key holds a non-empty `{…}`, when only the bytes between the first `{` and the next `}` are hashed.
Keys with the same hash tag share a slot, and therefore a node. A multi-key command, transaction or script whose keys
span slots fails with `CROSSSLOT`. Everything under one hash tag must fit on one shard, and the client's documentation
advises tagging by genuinely related data, such as a customer or tenant. Outside cluster mode there are no slots, and a
hash tag is just part of the key.

A keyspace is a cluster name and up to 8 typed segments, such as NCode.Identity's cell and tenant
([Design: Keyspace](../designs/coordination.md#keyspace)). A text segment holds up to 256 characters of any kind,
`{` and `}` included.

## Decision

**Every key of a keyspace carries the keyspace as its hash tag, so the whole keyspace lives in one slot with its
revision counter, its sessions and its indexes. The RESP provider declares `atomicity.across-partitions`, and a
cluster spreads load across keyspaces, not within one.**

1. **The hash tag.** Every server key the provider writes for a keyspace starts with `{`, the keyspace's encoded form
   and `}`. The encoding escapes `{`, `}` and its own escape character, so the first `}` always closes the tag, and two
   different keyspaces never share an encoded form. The rest of the key encoding is the provider's to define.
2. **Everything of a keyspace in its slot.** The keyspace's revision counter, its sessions' liveness keys, its fencing
   and sequence counters, and any index the provider keeps for listing or watching share the tag. So every script of
   ADR-1023 runs in one slot.
3. **Atomic across partitions.** Since a whole keyspace shares one slot, any operation within a keyspace is atomic, and
   the provider declares `atomicity.across-partitions`. A key's partition depth doesn't change where it is stored.
4. **Listing and watching from the keyspace's slot.** A listing reads an index kept in the keyspace's slot, and never
   scans the cluster.
5. **Scale by keyspace.** A cluster spreads keyspaces across shards by their slots. One keyspace's load stays on one
   shard. An application that needs to spread one tenant's coordination state splits it into several keyspaces. The
   provider's documentation says so, and names a keyspace per tenant as the usual unit.
6. **Every server mode.** The same keys serve a standalone server, a primary with replicas and a cluster.
   StackExchange.Redis routes a script by its keys, and follows `MOVED` and `ASK` while a slot migrates
   ([ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md)). A
   multi-key operation can fail while its slot migrates during manual resharding. The provider reports that failure as
   `unavailable`, with the outcome known not to have applied, since the server refuses the command before running it.

## Options considered

| Force | One slot per keyspace | One slot per partition | No cluster mode |
| --- | --- | --- | --- |
| F1 revision | One counter per keyspace, beside its records | One counter per partition, superseding ADR-1014 rule 1 | One counter per keyspace |
| F2 one slot per script | Always | Only within a partition; a session or revision outside it can't join | Always |
| F3 listing and watching | An index in one slot | A prefix that crosses partitions spans slots, with no single order | An index |
| F4 load | Across keyspaces | Across partitions too | One primary |
| F5 atomicity | Across partitions | Within a partition | Across partitions |
| Server modes | All | All | Standalone and primary with replicas only |

- **One slot per keyspace.** Chosen: it keeps ADR-1014 and ADR-1023 as they are, in every server mode, and spreads load
  the way the client's own guidance recommends, by tenant. Its limit is one keyspace's load on one shard. Coordination
  state is small, so a keyspace that outgrows a shard is a sign to split it, not to split the slot.
- **One slot per partition.** Rejected: a keyspace would have one revision per partition, which supersedes ADR-1014's
  keyspace-wide revision and leaves a prefix watch across partitions with no order. A write bound to a session couldn't
  check the session in the same script unless the session shared the partition. Partitions would decide placement for
  a gain, spreading one keyspace, that coordination state rarely needs.
- **No cluster mode.** Rejected: a host whose Valkey runs as a cluster couldn't use the provider, and the hash tag of
  rule 1 costs nothing outside cluster mode.
- **A revision kept apart from the records.** Rejected without a column: a counter in another slot can't be
  incremented in the same script as the record it numbers, so revisions and writes could disagree after a failure.

## Consequences

- Every RESP key carries its keyspace, so keys are longer by the keyspace's encoded form, at most a few hundred bytes for
  the longest keyspaces and a few dozen for typical ones.
- The notification decision can use sharded Pub/Sub (`SSUBSCRIBE`) per keyspace, since a keyspace's channels can share
  its slot.
- A keyspace is the unit of placement and scale, so applications choose keyspaces with load in mind. NCode.Identity's
  keyspace per tenant fits.
- The Garnet profile's cluster mode is verified by the conformance kit like Valkey's, since nothing here is specific to
  either server.
- You chose wrong if one keyspace's coordination load outgrows a shard in a real deployment, or if a consumer needs a
  watch across keyspaces. Then a record that supersedes this one weighs per-partition placement and revisions.

## References

- [Design: Keyspace](../designs/coordination.md#keyspace)
- [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
- `src/NCode.Coordination.Abstractions/Keys/Keyspace.cs`, `CoordinationKey.cs` and `KeySegment.cs`: the keyspace's
  limits and its partition depth
- [Valkey: Cluster specification](https://valkey.io/topics/cluster-spec/), read 2026-11-25: the key distribution model,
  hash tags, multi-key operations and resharding
- [StackExchange.Redis: Hash Tags and Slots](https://seredis.dev/HashTags), read 2026-11-25
