# 1030. The RESP provider's runs drop its own connections, restart and fail over servers of their own, and repeat every suite on clusters

- **Status:** Proposed
- **Date:** 2026-11-26
- **Deciders:** NCode Group
- **Amends:** 1019

## Context

[ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
rule 3 asks for injected faults: a dropped connection, a paused client, a server restart and a failover.
[ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md)
rule 7 gives records and notifications a dropped-connection run through the target's `DropConnections`, and leaves
restart and failover to the providers.
[ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md) rule 4 asks
for a test against a real server with injected faults.
[ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md) rule 6 says
the same keys serve every server mode.
[ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
rule 5 says a subscription resyncs after a restored connection, and its rule 4 after a number that goes backwards, as
after a failover. ADR-1024 expects the Garnet profile's cluster mode to be verified like Valkey's. Proving them meets
these forces:

- **F1. Shared servers.** The RESP tests share one Valkey and one Garnet server across test classes that run in
  parallel, so a fault that reaches every client of a server reaches every running test.
- **F2. Volatile servers.** A restarted in-memory server comes back empty, or with what it saved, so no single
  history spans a restart.
- **F3. Host ports.** Docker picks a host port on each start of a container unless the binding fixes it, so a
  restarted container is reachable at a different endpoint.
- **F4. Cluster nodes.** A cluster node announces its address to its clients and its peers, and its bus port is its
  port plus 10,000 unless `cluster-port` sets it. A node in a container announces a container address that the host
  can't reach on Docker Desktop.
- **F5. Garnet clusters.** The Garnet image carries no command-line client, so nothing in its container can join
  nodes into a cluster. A Garnet node serves clients and peers on one port, replicates only in cluster mode and from a
  primary's append-only file, and has no failover of its own: an outside control plane promotes a replica with
  `CLUSTER FAILOVER`.
- **F6. Failover.** Replication is asynchronous, so a replica promoted while its primary still takes writes misses
  those writes, and the old primary drops them when it follows the new one. Without Sentinel or a cluster, nothing
  tells StackExchange.Redis that a Valkey primary moved: it finds out at its next check of each server's role, every
  `configCheckSeconds`, 60 seconds by default.
- **F7. Closing checks.** A notification resync carries the last number delivered, not the number it resynced at, so a
  resync read while a run's closing publish is in flight may or may not cover it. A restored connection, or the gap the
  closing message itself reveals, can set one off there.

## Decision

**The RESP target drops only its own provider's connections, found by client name. A record watch run drops
connections like a notification run, in-process too. Restarts and failovers are tests of the RESP provider, each
against servers of its own on reserved host ports. A Valkey container and a Garnet container, each running three
cluster nodes, repeat every suite and prove each keyspace stays in its slot. A notification run publishes closing
messages until no subscriber's resync is in doubt.**

1. **Drop by client name.** The target's provider connects with a client name unique to the target
   (`conformance-<guid N>`). `DropConnections` opens a connection of its own with `AllowAdmin`, lists the clients on
   every server with `CLIENT LIST`, and kills each one with that name by `CLIENT KILL ID`, its subscription connection
   included. It fails when it kills none. The client reconnects and both of its connections resubscribe.
2. **Record watches drop too.** The kit's record watch suite has `ConcurrentRun_WhenConnectionsDrop_DeliversAsDeclared`,
   run like the notification suite's, so watches show their resync under drops. This amends ADR-1019 rule 7. The
   in-process provider's test decorator drops record watches too: in place of a watch's next change after a drop it
   gives a resync at that change's revision, so the run runs without Docker. Against the in-process provider alone it
   skips.
3. **Restarts in the RESP tests.** Each restart test starts a server of its own on a host port reserved before it
   starts, so a stop and a start keep its endpoint (F3), and no other test meets the restart (F1). Three clients
   create records while the server stops and starts. Every failure is `unavailable`, and each create that failed
   with `OutcomeUnknown` false is absent afterwards. The same provider answers again. A notification subscription and a
   prefix watch opened before the restart resync, and then deliver a later message or write. Valkey runs with its
   default save, so the check that a call reported unapplied is absent is meaningful there. Garnet runs
   `--lua --lua-transaction-mode true` and comes back empty.
4. **A Valkey cluster.** One container from the pinned Valkey image runs three nodes. Each node has a host port
   reserved before it starts and publishes it as itself, announces `127.0.0.1`, uses a bus port set by
   `--cluster-port` (16380 to 16382) and keeps no persistence (F4). The nodes are joined with
   `valkey-cli --cluster create ... --cluster-replicas 0 --cluster-yes`, and the cluster endpoint uses `127.0.0.1`,
   the announced address, so the client sees each node once. Every suite runs against it through a
   `ValkeyCluster<Primitive>ContractTests` class. Tests check that every key of a keyspace is in its slot on one
   node, and that each keyspace's sharded channel is subscribed only on its slot's node.
5. **A Garnet cluster.** One container from the pinned Garnet image runs three nodes with `--cluster`, each on a host
   port reserved before it starts and published as itself, announcing `127.0.0.1`, with the Lua arguments the profile
   needs, a checkpoint folder of its own and no append-only file. The tests join them (F5): `CLUSTER SET-CONFIG-EPOCH`
   on each, `CLUSTER ADDSLOTSRANGE` splitting the slots three ways and `CLUSTER MEET` from the first, then wait until
   every node reports `cluster_state:ok` and knows the other two. Every suite runs against it through a
   `GarnetCluster<Primitive>ContractTests` class. Tests check that every key of a keyspace is in its slot on one node,
   that a keyspace on each node gets its notifications through one node's subscription to its plain channel, and that
   a plain message published on every node reaches a subscriber on one. Where the client subscribes a plain channel is
   not asserted: StackExchange.Redis chose the slot's node in one run and other nodes in another.
6. **Failover.** Each failover test starts a primary and a replica of its own on reserved host ports: on Valkey, two
   servers in one container, the second started with `--replicaof`; on Garnet, a cluster of a primary that serves
   every slot and its replica, both with `--aof` (F5). Once the replica has caught up, the test promotes it without
   the primary's consent, with `REPLICAOF NO ONE` or `CLUSTER FAILOVER FORCE`, writes through the provider to the old
   primary, then demotes the old primary: with `REPLICAOF` on Valkey, and on Garnet by waiting until gossip takes its
   slots. The provider connects with `configCheckSeconds=1` (F6). A record written before the promotion survives, the
   one the old primary acknowledged after it is gone, and every failure while the provider finds the new primary is
   `unavailable`. A notification subscription and a prefix watch that received the write the failover took back resync
   on the next number, which repeats it, and then deliver later messages and writes (ADR-1025 rule 4).
7. **Closing messages.** A notification run publishes a closing message and waits for each subscriber to read it, or a
   resync read after the publish returned. When a subscriber's first answer is a resync read while the publish was in
   flight (F7), the run publishes another closing message, 100 milliseconds later, until every subscriber settles, for
   at most the suite's 10-second limit. A resync read before the publish began never answers it. This amends ADR-1019
   rule 5.
8. **Declarations stand.** Every run passes, on both profiles, on both clusters and across a failover, so the
   profiles' declared guarantees don't change: every primitive but notifications is linearizable until a failover,
   which can lose acknowledged writes, and the failover tests show such a loss. A run that fails is a finding for the
   provider, not a reason to change a declaration.

## Options considered

- **Restarts in the kit.** Rejected: the checker can't search a history across a volatile restart (F2). A durable
  provider may add one.
- **`CLIENT KILL TYPE`.** Rejected: it kills the clients of every test on a shared server (F1).
- **The client's simulated failure.** Rejected: ADR-1022 rule 4 asks for a real server's fault.
- **Three cluster containers on a Docker network.** Rejected: their nodes announce container addresses that the host
  can't reach on Docker Desktop (F4).
- **A cluster per test.** Rejected: its start-up cost.
- **Joining the Garnet nodes with `valkey-cli` from a second container that shares the Garnet container's network.**
  Rejected: it couples two containers for one cluster, and the client the tests already use can join the nodes.
- **A coordinated failover (`FAILOVER`, `CLUSTER FAILOVER` with no option).** Rejected: it waits for the replica to
  catch up, so it shows nothing of what a failover can lose.
- **The client's default role check.** Rejected: a Valkey failover test would wait up to 60 seconds for it, failing
  every write meanwhile (F6).
- **Accepting any resync read after the closing publish began.** Rejected: one read before the publish applied doesn't
  cover it, so a lost closing message would pass (F7).
- **Asserting the node on which the client subscribes a plain channel.** Rejected: it varies between runs, so the
  tests publish on every node instead.

## Consequences

- The dropped-connection runs run on Valkey, Garnet and both clusters, and a record watch's resync is checked under
  drops, in-process too.
- Garnet's cluster mode and a failover of Valkey and of a Garnet cluster are verified. Under concurrent publishes,
  Garnet cluster subscribers resync more often, each time with a resync, as the profile declares.
- The failovers show that a write the old primary acknowledged after the promotion is lost, as
  `consistency.linearizable-until-failover` allows, and that subscriptions resync when numbers go back.
- A host whose Valkey fails over without Sentinel or a cluster sees writes fail as `unavailable` until
  StackExchange.Redis's role check finds the new primary, every `configCheckSeconds`.
- The clusters and the restart and failover tests need a Docker daemon on the machine that runs the tests.
- You chose wrong if a restart or failover test fails for a reason the provider can't see, such as gossip reaching the
  old Garnet primary before the test's write, or if a remote Docker host must run the clusters.

## References

- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md),
  [ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
- [Valkey: Cluster tutorial](https://valkey.io/topics/cluster-tutorial/) and
  [Valkey: CLIENT KILL](https://valkey.io/commands/client-kill/), read 2026-11-26
- [Garnet: Cluster overview](https://microsoft.github.io/garnet/docs/cluster/overview),
  [Garnet: Cluster commands](https://microsoft.github.io/garnet/docs/commands/cluster) and
  [Garnet: Cluster replication](https://microsoft.github.io/garnet/docs/cluster/replication), read 2026-11-26
- [Valkey: FAILOVER](https://valkey.io/commands/failover/) and
  [Valkey: REPLICAOF](https://valkey.io/commands/replicaof/), read 2026-11-26
- [StackExchange.Redis: Configuration](https://seredis.dev/Configuration), read 2026-11-26
- The public API metadata of StackExchange.Redis 3.4.35 and Testcontainers 4.17.0
- A test run on 2026-11-26 against `valkey/valkey:9.2.0` and `ghcr.io/microsoft/garnet:2.2.13`, and the command-line
  help of `ghcr.io/microsoft/garnet:2.2.13`
