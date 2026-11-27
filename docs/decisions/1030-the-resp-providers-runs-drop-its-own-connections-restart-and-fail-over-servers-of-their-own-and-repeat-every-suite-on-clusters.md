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
rule 5 says a subscription resyncs after a restored connection. Proving them meets these forces:

- **F1. Shared servers.** The RESP tests share one Valkey and one Garnet server across test classes that run in
  parallel, so a fault that reaches every client of a server reaches every running test.
- **F2. Volatile servers.** A restarted in-memory server comes back empty, or with what it saved, so no single
  history spans a restart.
- **F3. Host ports.** Docker picks a host port on each start of a container unless the binding fixes it, so a
  restarted container is reachable at a different endpoint.
- **F4. Cluster nodes.** A cluster node announces its address to its clients and its peers, and its bus port is its
  port plus 10,000 unless `cluster-port` sets it. A node in a container announces a container address that the host
  can't reach on Docker Desktop.

## Decision

**The RESP target drops only its own provider's connections, found by client name. A record watch run drops
connections like a notification run. Restarts are tests of the RESP provider, each against a server of its own on a
reserved host port. One Valkey container running three cluster nodes repeats every suite and proves each keyspace
stays in its slot.**

1. **Drop by client name.** The target's provider connects with a client name unique to the target
   (`conformance-<guid N>`). `DropConnections` opens a connection of its own with `AllowAdmin`, lists the clients on
   every server with `CLIENT LIST`, and kills each one with that name by `CLIENT KILL ID`, its subscription connection
   included. It fails when it kills none. The client reconnects and both of its connections resubscribe.
2. **Record watches drop too.** The kit's record watch suite has `ConcurrentRun_WhenConnectionsDrop_DeliversAsDeclared`,
   run like the notification suite's, so watches show their resync under drops. This amends ADR-1019 rule 7. The
   in-process target drops nothing, so its run skips.
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
5. **Declarations stand.** Every run passes, so the profiles' declared guarantees don't change. A run that fails is
   a finding for the provider, not a reason to change a declaration.

## Options considered

- **Restarts in the kit.** Rejected: the checker can't search a history across a volatile restart (F2). A durable
  provider may add one.
- **`CLIENT KILL TYPE`.** Rejected: it kills the clients of every test on a shared server (F1).
- **The client's simulated failure.** Rejected: ADR-1022 rule 4 asks for a real server's fault.
- **Three cluster containers on a Docker network.** Rejected: their nodes announce container addresses that the host
  can't reach on Docker Desktop (F4).
- **A cluster per test.** Rejected: its start-up cost.

## Consequences

- The dropped-connection runs run on Valkey, Garnet and the cluster, and a record watch's resync is checked under
  drops.
- The in-process record watch target drops nothing, so its drop run skips.
- The cluster needs a Docker daemon on the machine that runs the tests.
- Garnet's cluster mode and failover stay unverified.
- You chose wrong if a restart test fails for a reason the provider can't see, or if a remote Docker host must run
  the cluster.

## References

- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md),
  [ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
- [Valkey: Cluster tutorial](https://valkey.io/topics/cluster-tutorial/) and
  [Valkey: CLIENT KILL](https://valkey.io/commands/client-kill/), read 2026-11-26
- The public API metadata of StackExchange.Redis 3.4.35 and Testcontainers 4.17.0
- A test run on 2026-11-26 against `valkey/valkey:9.2.0` and `ghcr.io/microsoft/garnet:2.2.13`
