# Design: NCode.Coordination

- **Status:** In progress (phase 4 of 7)
- **Date:** 2026-11-24
- **Issue:** [#4](https://github.com/NCodeGroup/coordination/issues/4)
- **Authors:** NCode Group

## Summary

NCode.Coordination is a general-purpose .NET library that gives the nodes of a cluster the agreement they need on
liveness, ownership, one-time events, change and usage, through one programming model over interchangeable providers.
Each provider speaks a standard protocol, declares the guarantees it keeps, and proves them against one conformance
kit. The central decisions are the product's scope ([D1](#d1-scope)), its originality ([D2](#d2-originality)),
declared guarantees ([D3](#d3-guarantees)), protocol-named providers ([D4](#d4-providers)), sessions with fencing
tokens ([D5](#d5-sessions-and-fencing)), sequenced notifications without brokering ([D6](#d6-notifications)), one
conformance kit ([D7](#d7-conformance)), the first stable release's scope ([D8](#d8-first-stable-release)), expiry as a
duration on a declared clock ([D9](#d9-expiry-clock)), the shapes of the primitives' contracts
([D10](#d10-contract-values) to [D15](#d15-counting-sequences-and-notifications)), the composition that binds
providers and checks requirements when the host starts ([D16](#d16-composition)), the conformance kit's contract
suites ([D17](#d17-conformance-kit)), the holder's local deadline ([D18](#d18-holder-deadline)), the history checker
that verifies concurrent runs of every primitive ([D19](#d19-history-checker)), the recipes built on the primitives'
contracts ([D20](#d20-recipes)), the hosting that ties a node's session to its host's lifetime ([D21](#d21-hosting)),
and the RESP provider's client, atomic scripts, cluster slots, numbered notifications, largest count and record table
([D22](#d22-resp-client) to [D27](#d27-resp-records)).

## Goals and Non-Goals

**Goals**

- **G1.** Every coordination need of NCode.Identity, the first consumer, is met by a primitive or a recipe, with the
  guarantee that need relies on stated and tested ([Context](#context)).
- **G2.** The coordination problems services commonly meet are covered whether or not the first consumer meets them:
  semaphores, read-write locks, barriers, partition ownership, a service registry, transactions, sequences, smoother
  rate limiters, single-flight.
- **G3.** A host chooses its provider from the servers it already runs — a RESP server, PostgreSQL, etcd, Consul — and
  a host with one node needs none.
- **G4.** A feature bound to a provider that cannot keep its required guarantee fails at startup, never in production.
- **G5.** Every guarantee a provider declares is verified against a real server, under concurrency and injected faults.
- **G6.** No part of the product is a derivative work of another project: every design is traceable to published
  specifications, papers or documentation, and every dependency is permissively licensed and used through its public
  API.

**Non-goals**

- **A general message broker.** Durable queues, routing, consumer groups, retries and sagas belong to a broker or a job
  dispatcher; notifications here are coordination signals.
- **A database or a cache.** Records are small and few per key, and coordination state is never the only copy of
  business data.
- **Actors, RPC between nodes, DNS service discovery or configuration management.** Orleans, Akka and Pekko inform the
  membership and singleton concepts; their programming models are not reproduced.
- **A new storage server.** The product does not reimplement Redis, Valkey, etcd or ZooKeeper; a native server is a
  late, optional phase taken only for a need no provider meets.

## Context

A service that runs as several nodes must agree on a small amount of state. Getting it wrong is a correctness or
security defect: a token accepted twice, a refresh token presented twice, a background pass run by two nodes at once, a
settings change one node never heard about.

**The first consumer.** NCode.Identity, an OpenID Connect and OAuth 2.0 server family, builds these agreements from
five ports — an expiring key set, value set, index set, counter set and a best-effort broadcaster — with an in-process
and a Valkey adapter. Its needs are the minimum the first stable release meets:

| Identity need                                                                              | Primitive                  |
| ------------------------------------------------------------------------------------------ | -------------------------- |
| DPoP proof and client-assertion `jti` accepted once (RFC 9449, RFC 7523)                    | Occurrences                |
| Server-issued DPoP nonces recognized by any node                                           | Occurrences                |
| A login transaction redeemed exactly once                                                  | Records (take)             |
| A session updated by compare-and-swap, with a sliding expiry                               | Records                    |
| Sessions found by the provider's `sid` or `sub` for back-channel logout                    | Sets                       |
| A refresh token presented at most once across nodes                                        | Leases with fencing tokens |
| Rate limits and daily quotas counted across nodes                                          | Counters                   |
| A periodic pass (grant pruning, session termination, key rollover) run by one node per interval | Claims                |
| Tenant, team, platform-settings and master-key changes reaching every node                 | Notifications              |
| A node that may have missed a change knowing so, and revalidating                          | Notifications (resync)     |

Several of these are built on ports not shaped for them: "one node runs each interval" is a create-only key whose
lifetime each feature tunes; a refresh lease is a value whose owner token is compared on release; a missed broadcast is
assumed whenever a connection is restored. Each construction is correct only under conditions the port does not state.

**Beyond the first consumer.** Other services meet problems Identity does not yet:

| Need                                                                              | Primitive or recipe                  |
| --------------------------------------------------------------------------------- | ------------------------------------ |
| At most N holders of a resource, such as connections to a fragile system          | Semaphores                           |
| Many readers or one writer                                                        | Read-write locks                     |
| Every node of a group reaching a point before any proceeds                        | Barriers                             |
| Which node owns each partition of a workload, rebalanced as nodes come and go      | Partition ownership                  |
| Nodes finding each other's addresses and metadata                                 | Membership (service registry)        |
| Configuration that nodes read and watch under a prefix                            | Records with prefix watches          |
| Several keys changed together or not at all, on conditions                        | Transactions                         |
| Unique, increasing numbers across the cluster                                     | Sequences                            |
| Smoother limits than a fixed window                                               | Sliding-window and token-bucket rate limiters |
| One fetch of an expensive value while other nodes wait for it                     | Single-flight                        |

**Servers differ in what they keep.** A single-primary server with asynchronous replicas is linearizable until it
fails over and may then lose acknowledged writes; a consensus store keeps its promises across failover; a
multi-threaded RESP server may run a script atomically only in a mode it must be started with; a coordination server
with no replicas loses a member's keys when that member crashes. The design must make these differences visible.

## Design

### Keyspace

Every primitive lives in a keyspace: a cluster name, then structured segments (for Identity, a cell and a tenant). A key
is a sequence of typed segments, never a string the caller concatenates, so two keys never collide by accident and a
provider encodes them for its own server. A key may name a partition segment, which a provider uses to keep related
keys together where atomicity across them needs it.

### Primitives

- **Session.** A node opens a session with a time to live and keeps it alive. When the session ends, by a graceful
  close or by expiry, everything bound to it ends too.
- **Membership.** The live sessions of a cluster, each with the node's identity, address and metadata, readable and
  watchable. Partition ownership is a recipe over it: each partition goes to one live member by a deterministic
  assignment, and moves only when membership changes.
- **Lease.** Exclusive ownership of a named resource for a bounded time, held by a session or by an owner token. Every
  grant carries a fencing token that increases with each grant of that resource.
- **Semaphore.** A lease with N slots, each slot with its own fencing token.
- **Election.** A lease on a role, with followers watching it; the leader's term is its fencing token.
- **Claim.** At most one holder per named window, such as the pass of one interval, expiring with the window.
- **Record.** A small value under a key, with a revision that changes on every write: create-if-absent, read,
  compare-and-swap on revision, compare-and-delete, take, and an expiry or a session binding. Records under a prefix
  can be listed and watched.
- **Transaction.** Conditions on several records' revisions or values, then one set of writes when they hold and
  another when they do not, within one partition where the provider requires it.
- **Occurrence.** A key recorded at most once until it expires: create-if-absent with no value. It is separate from
  records because it is the hottest path and fails closed when the provider cannot answer.
- **Set.** Members under a key, each with its own expiry: add, remove, list, clear.
- **Counter.** A count under a key that spans one window: increment, read, reset, and an ordered batch that stops at
  the first counter over its limit.
- **Sequence.** A named counter that hands out unique, strictly increasing numbers and never resets.
- **Notification.** A message on a topic, delivered to every subscribed node at most once, numbered by a per-topic
  sequence, and followed by a resync signal whenever a subscriber may have missed one. A watch on records is the same
  shape, with the records' revision as the sequence.

Recipes over the primitives: locks, read-write locks, barriers, singletons, partition ownership, single-flight, rate
limiters and cache invalidation.

### Guarantees

Each primitive's operations carry guarantees that a provider declares and the conformance kit verifies:

| Dimension             | Levels                                                                                    |
| --------------------- | ----------------------------------------------------------------------------------------- |
| Consistency           | Linearizable across failover; linearizable until failover; local to one process            |
| Durability            | Volatile; asynchronously replicated; durable on a quorum or disk                          |
| Expiry authority      | The server's clock; the caller's clock                                                    |
| Notification delivery | At most once with gap detection; at most once                                             |

A feature declares the guarantees it requires; the composition refuses at startup to bind it to a provider that does
not declare them. A host may bind different primitives, or keyspaces, to different providers. A provider that cannot
keep a primitive honestly declares it unsupported. The in-process provider implements every primitive.

### Packages

The shape follows the transport model of messaging libraries such as MassTransit: one programming model, providers that
plug in through a configurator of their own, an in-process provider, and one test harness.

| Package                           | Holds                                                                          |
| --------------------------------- | ------------------------------------------------------------------------------ |
| `NCode.Coordination.Abstractions` | The primitives' contracts, keys, guarantees and requirements                   |
| `NCode.Coordination`              | Composition, requirement checking, recipes, telemetry, the in-process provider  |
| `NCode.Coordination.Hosting`      | The session bound to the host's lifetime, graceful leave, health checks        |
| `NCode.Coordination.Resp`         | The RESP provider, with Valkey and Garnet profiles                              |
| `NCode.Coordination.PostgreSql`   | The PostgreSQL provider                                                        |
| `NCode.Coordination.Etcd`         | The etcd v3 provider                                                           |
| `NCode.Coordination.Consul`       | The Consul provider                                                            |
| `NCode.Coordination.Testing`      | The conformance kit, fault injection and the history checker                   |

The core depends on the `Microsoft.Extensions` abstractions and the .NET runtime's `System.Threading.RateLimiting` only.
The hosting package adds the `Microsoft.Extensions` options, logging and health checks packages.
A provider depends on its protocol's client library, consumed as a permissively licensed package through its public API.

## Security Analysis

| Threat | Mitigation | What remains |
| --- | --- | --- |
| A replayed one-time value is accepted because the provider lost the occurrence on failover | Occurrences declare their consistency; a feature requires what it needs; values expire with the token they guard | Loss within the declared bound, accepted by the feature that chose the provider |
| A paused node writes after its lease passed to another | Fencing tokens, checked by the resource that takes the write | A resource that does not check the token |
| A tenant reads or overwrites another's coordination state on a shared server | Structured keys with the tenant as a segment; provider credentials scoped to the cluster's keyspace | A server shared with untrusted clients holding the same credentials |
| Coordination traffic is read or altered on the network | Providers require TLS outside local development, and authenticate to the server | Servers configured without TLS by their operator |
| A value in coordination state discloses a secret | Values are opaque bytes the caller encrypts where they must not be readable | Metadata such as key names and sizes |

## Failure Modes

| Failure | Impact | Detection | Response |
| --- | --- | --- | --- |
| The provider is unreachable | Operations throw; occurrences fail closed | Health check, operation metrics | Callers fail their request; the host reports not ready |
| A node crashes | Its session expires; its leases and ephemeral records end with it | Membership watch | Another node takes the leases once the session's time to live passes |
| A node pauses past its session's time to live | Its leases pass to others while it believes it holds them | Fencing token refused by the resource | The paused node's writes are refused; it reacquires |
| A subscriber misses notifications | Its caches may be stale | A gap in the topic's sequence, or a reconnect | The resync signal; the handler rereads what the topic covers |
| The server fails over and loses acknowledged writes | Occurrences, counters or leases written just before are lost | Declared in the provider's guarantees; verified by the conformance kit | Features that cannot accept it require a provider that keeps its writes across failover |

## Decisions

### D1. Scope

NCode.Coordination is a general coordination library for the nodes of a cluster, with its first consumer as a floor,
not a ceiling. [ADR-1000](../decisions/1000-ncode-coordination-is-a-general-coordination-library-for-the-nodes-of-a-cluster.md)

### D2. Originality

The product is an original work, and a clean-room method keeps it free of derivative work.
[ADR-1001](../decisions/1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md),
[ORIGINALITY.md](../../ORIGINALITY.md)

### D3. Guarantees

Primitives declare guarantees, and the composition refuses a requirement no provider meets.
[ADR-1002](../decisions/1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)

### D4. Providers

Providers are named for the protocol they speak and carry a profile per vendor.
[ADR-1003](../decisions/1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)

### D5. Sessions and fencing

A node holds a session that binds its leases and membership, and every lease grant carries a fencing token.
[ADR-1004](../decisions/1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)

### D6. Notifications

Notifications carry a sequence and tell a subscriber to resync, and the product brokers no messages.
[ADR-1005](../decisions/1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md)

### D7. Conformance

Every provider passes one conformance kit that checks recorded histories against each guarantee.
[ADR-1006](../decisions/1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)

### D8. First stable release

The first stable release carries every primitive and recipe of the design, implemented by the in-process, RESP and
PostgreSQL providers and verified by the conformance kit, and is cut after NCode.Identity runs on prerelease packages.
The etcd and Consul providers, further vendor profiles and the native server follow as additive minor releases, and
each contract is checked against etcd's and Consul's public API documentation while it is designed, so they fit it.
[ADR-1007](../decisions/1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md)

### D9. Expiry clock

Every expiry is a duration, measured on the clock the provider declares as its expiry authority, the server's wherever
the server can, and rounded up to the server's units. A feature whose lifetime is an instant, such as a token's `exp` or
a window's end, converts it on its own clock and adds an allowance for skew between nodes; holders of time-bound
ownership give it up at a conservative local deadline and rely on fencing tokens.
[ADR-1008](../decisions/1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)

### D10. Contract values

Every value of the contracts is a sealed record class, so no default instance skips validation.
[ADR-1010](../decisions/1010-vocabulary-values-are-sealed-record-classes-so-no-default-instance-skips-validation.md)

### D11. Outcomes and failures

An operation returns its outcome, and throws a coded failure that says whether it may have taken effect.
[ADR-1011](../decisions/1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)

### D12. Primitive interfaces and operations

Each primitive is an interface whose operations are named with the guarantee dimensions they carry.
[ADR-1012](../decisions/1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)

### D13. Ownership

Ownership is granted with a fencing token and an owner token, and bound to a session or a time to live.
[ADR-1013](../decisions/1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)

### D14. Records, watches and transactions

Records carry a keyspace-wide revision, and watches and transactions are written in it.
[ADR-1014](../decisions/1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)

### D15. Counting, sequences and notifications

Occurrences fail closed, windows align to the epoch, and notifications number each topic's messages.
[ADR-1015](../decisions/1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)

### D16. Composition

A host registers providers on one builder and binds each to primitives or to keyspaces; its start fails on every
requirement a binding cannot keep, and every operation is traced and measured without its keys.
[ADR-1016](../decisions/1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md)

### D17. Conformance kit

The conformance kit is a set of xUnit contract classes, one per primitive, that a provider's test project derives from,
giving the provider under test and a way to advance its clock; a test skips what the provider declares unsupported.
[ADR-1017](../decisions/1017-the-conformance-kit-is-a-set-of-xunit-contract-classes-a-providers-tests-derive-from.md)

### D18. Holder deadline

A holder begins a deadline on its own clock before each request that grants or renews its ownership, ending an allowance
for drift before the requested time to live, and treats the ownership as lost once it passes.
[ADR-1018](../decisions/1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md)

### D19. History checker

Each primitive's suite runs concurrent clients against the provider and records every call and return; a checker then
searches each key's history, or the one history of the keys a listing, a multi-key counter batch or a transaction spans,
for a linearization against the primitive's sequential specification, treating a call whose outcome is unknown as
possibly applied, and checks notifications and record watches against their declared delivery. A failure is reported
cut to its shortest failing prefix. A paused-holder run and dropped-connection runs add the faults a provider's declared
guarantees must survive.
[ADR-1019](../decisions/1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md)

### D20. Recipes

Each recipe is an interface whose implementation calls only the primitives' services, declares the primitives and
guarantees it relies on so a host requires them with `RequireRecipe`, and waits on releases and watches, never on a
timer. The rate limiters are `System.Threading.RateLimiting` limiters.
[ADR-1020](../decisions/1020-recipes-use-only-the-primitives-contracts-declare-the-guarantees-they-rely-on-and-wait-on-releases-and-watches.md)

### D21. Hosting

The host opens one session once its composition's requirements pass, joins its cluster's membership with it, renews it
on its own clock and opens another when it is lost, and closes it once every hosted service has stopped; features take
the session from `IHostSessionService`, and a health check reports the host not ready while it holds none.
[ADR-1021](../decisions/1021-a-host-holds-one-session-for-its-lifetime-joins-its-membership-with-it-and-closes-it-as-it-stops.md)

### D22. RESP client

The RESP provider speaks through StackExchange.Redis, the client Microsoft's own packages for the protocol use,
behind one internal seam, and can share the host's connection.
[ADR-1022](../decisions/1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md)

### D23. RESP atomicity

Each operation is one command or one Lua script that receives all its keys and checks before it writes; the provider
uses no client-side transaction, no functions and no vendor-specific conditional command.
[ADR-1023](../decisions/1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)

### D24. RESP slots

Every key carries its keyspace as its cluster hash tag, so a keyspace, its revision counter and its indexes share one
slot, and any operation within a keyspace is atomic.
[ADR-1024](../decisions/1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md)

### D25. RESP notifications

The script that makes a change numbers and publishes it, subscribers resync on a gap, a reconnect or a failed
periodic check, and state a watch can see expires through scripts.
[ADR-1025](../decisions/1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)

### D26. RESP counts

A RESP counter keeps counts up to 2^53 − 1, the largest integer a Lua number holds exactly, so its script checks
every count before it writes; an increment past it fails as `limit-exceeded` and applies nothing.
[ADR-1026](../decisions/1026-resp-counters-keep-counts-up-to-the-largest-integer-a-lua-number-holds-exactly.md)

### D27. RESP records

A keyspace's records live in one hash, an index and a set of deadlines beside its revision, so every record script
receives every key it touches; the index holds a digest of each key segment, so a prefix lists as one range, each
page examining at most its limit plus 64 members, with a short continuation token; and every change, a take and an
expiry included, is a script that numbers and publishes it.
[ADR-1027](../decisions/1027-resp-records-live-in-one-table-per-keyspace-indexed-by-digests-of-their-segments.md)

### D28. RESP ownership

A keyspace's sessions, grants and members live in fixed keys beside its record table; every keyspace script receives
all twelve and first ends expired sessions and grants; a session's end removes what is bound to it through one index;
and a claim is one `SET` with `NX` and the caller's time to live.
[ADR-1028](../decisions/1028-resp-ownership-state-lives-in-fixed-keys-per-keyspace-bound-to-sessions-through-one-index.md)

### D29. RESP subscriptions

Each notification subscription and watch reads its channel through one ordered queue and checks its number every
interval; a record watch reads each written record from the table and delivers it at the event's revision; and a
membership change names its group in the keyspace's event, so a membership watch lists its group only when it
changes.
[ADR-1029](../decisions/1029-resp-watches-read-each-written-record-from-the-table-and-membership-events-name-their-group.md)

### D30. RESP proof

The RESP provider's conformance targets drop only their own provider's connections, by client name; its own tests
restart servers of their own mid-run, where every failure is unavailable, no call reported unapplied applies, and every
subscription and watch resyncs; they fail over a Valkey primary and a Garnet cluster's primary of their own while the
primary still takes writes, where only the writes the old primary took after the promotion are lost and every
subscription and watch resyncs when its numbers go back; and every suite runs again against a Valkey cluster and a
Garnet cluster of three nodes, with tests that each keyspace keeps its keys in its slot, its Valkey sharded channel on
that slot's node, and its Garnet plain channel delivering from any node. The record watch suite has a
dropped-connection run too, which the in-process provider's test decorator runs, and a notification run publishes
further closing messages while a resync might have covered the last one.
[ADR-1030](../decisions/1030-the-resp-providers-runs-drop-its-own-connections-restart-and-fail-over-servers-of-their-own-and-repeat-every-suite-on-clusters.md)

## Open Questions

- **Q1.** How NCode.Identity consumes the packages while both repositories change together: prerelease packages from a
  feed, or a source dependency.
- **Q2.** Whether the etcd provider takes a permissively licensed .NET client package or writes its own gRPC message
  definitions from the etcd API reference; copying etcd's own `.proto` files is ruled out, because they are another
  project's files.
- **Q3.** Which capabilities beyond NCode.Identity's needs the first stable release carries, and which follow it.
  Decided: [D8](#d8-first-stable-release),
  [ADR-1007](../decisions/1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md).
- **Q4.** Whether expiry is evaluated on the server's clock for every provider that can, or on the caller's where a
  feature needs an absolute instant. Decided: [D9](#d9-expiry-clock),
  [ADR-1008](../decisions/1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md).

## Rollout and Phases

1. **Foundation.** The repository, this design, the originality policy and the founding decisions.
2. **Model.** The abstractions, the in-process provider and the conformance kit with its history checker, and the
   license check [ORIGINALITY.md](../../ORIGINALITY.md) requires in CI, in eight plans:
   the abstractions foundation with the CI checks ([#6](https://github.com/NCodeGroup/coordination/issues/6)), the
   primitives' contracts ([#7](https://github.com/NCodeGroup/coordination/issues/7)), the composition
   ([#8](https://github.com/NCodeGroup/coordination/issues/8)), the in-process provider with the kit's contract tests
   ([#9](https://github.com/NCodeGroup/coordination/issues/9), [#10](https://github.com/NCodeGroup/coordination/issues/10)),
   the history checker ([#11](https://github.com/NCodeGroup/coordination/issues/11)), the recipes
   ([#12](https://github.com/NCodeGroup/coordination/issues/12)) and hosting
   ([#13](https://github.com/NCodeGroup/coordination/issues/13)).
3. **RESP.** The RESP provider with Valkey and Garnet profiles, covering every NCode.Identity need, with gap-detecting
   notifications, in six plans: the provider foundation with the Valkey and Garnet profiles and a server test harness
   ([#24](https://github.com/NCodeGroup/coordination/issues/24)), occurrences, counters, sequences and sets
   ([#25](https://github.com/NCodeGroup/coordination/issues/25)), records and transactions
   ([#26](https://github.com/NCodeGroup/coordination/issues/26)), sessions, membership, leases, semaphores, elections
   and claims ([#27](https://github.com/NCodeGroup/coordination/issues/27)), notifications and watches with gap
   detection and resync ([#28](https://github.com/NCodeGroup/coordination/issues/28)), and concurrent conformance
   runs, injected faults and cluster mode ([#29](https://github.com/NCodeGroup/coordination/issues/29)).
4. **PostgreSQL.** A provider that needs no infrastructure beyond the database a service already runs. NCode.Identity's
   hosted platform runs PostgreSQL as a managed three-node service in the cluster's datacenter
   ([hosting analysis](https://github.com/NCodeGroup/identity/blob/main/docs/hosting-analysis.md)), so the provider's
   declaration for a managed service follows how that service replicates and fails over, read from its documentation
   and verified by the conformance kit. It is built on four records, its client
   ([ADR-1031](../decisions/1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md)),
   its operations
   ([ADR-1032](../decisions/1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md)),
   its tables
   ([ADR-1033](../decisions/1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md))
   and its notifications and watches
   ([ADR-1034](../decisions/1034-postgresql-notifications-and-watches-are-numbered-in-the-writing-transaction-kept-in-a-bounded-log-and-woken-by-notify.md)),
   in six plans: the provider foundation with the profiles, the schema's migrations and a server test harness
   ([#41](https://github.com/NCodeGroup/coordination/issues/41)), occurrences, counters, sequences and sets
   ([#42](https://github.com/NCodeGroup/coordination/issues/42)), records and transactions with the keyspace revision,
   the change log and expiry ([#43](https://github.com/NCodeGroup/coordination/issues/43)), sessions, membership,
   leases, semaphores, elections and claims ([#44](https://github.com/NCodeGroup/coordination/issues/44)),
   notifications and watches with replay from the log, gap detection and resync
   ([#45](https://github.com/NCodeGroup/coordination/issues/45)), and concurrent conformance runs, injected faults and
   failover ([#46](https://github.com/NCodeGroup/coordination/issues/46)). Moving the provider to Npgsql 11 follows once
   its hosts can ([#47](https://github.com/NCodeGroup/coordination/issues/47)).
5. **First-consumer adoption.** NCode.Identity depends on NCode.Coordination and retires its shared-state packages;
   its interval passes and refresh lease become claims and fenced leases. This phase is planned and landed in
   NCode.Identity, and the first stable release follows it ([D8](#d8-first-stable-release)).
6. **etcd and Consul.** Providers whose servers keep sessions, leases and watches natively, linearizable across
   failover. On the first consumer's hosting, at least three small Kubernetes nodes in one datacenter whose first limit
   is memory, an etcd cluster adds three members, nodes with room for them and storage with a low sync latency,
   measured before it is relied on. The phase follows a consumer's need for a guarantee that holds across failover,
   and a distributed SQL database that speaks PostgreSQL's protocol, through the PostgreSQL provider, is the
   alternative to weigh then.
7. **Native server, if needed.** A coordination server of the product's own, built from the published consensus
   literature, only for a need no provider meets.

## References

- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006
- Hunt, Konar, Junqueira, Reed, "ZooKeeper: Wait-free coordination for Internet-scale systems", USENIX ATC 2010
- Gray, Cheriton, "Leases: An efficient fault-tolerant mechanism for distributed file cache consistency", SOSP 1989
- Herlihy, Wing, "Linearizability: A correctness condition for concurrent objects", TOPLAS 1990
- Kleppmann, "Designing Data-Intensive Applications", O'Reilly 2017
- [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519), JSON Web Token, and [RFC 9449](https://www.rfc-editor.org/rfc/rfc9449),
  OAuth 2.0 Demonstrating Proof of Possession, on the lifetimes of one-time values
- [Provenance](../provenance.md): the documentation consulted for this design
