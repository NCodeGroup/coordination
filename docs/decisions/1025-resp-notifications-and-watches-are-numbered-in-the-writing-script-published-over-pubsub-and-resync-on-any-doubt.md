# 1025. RESP notifications and watches are numbered in the writing script, published over Pub/Sub, and resync on any doubt

- **Status:** Proposed (amended by 1027, 1028)
- **Date:** 2026-11-25
- **Deciders:** NCode Group
- **Amends:** 1023

## Context

Notifications and watches have to tell a subscriber when it may have missed something:

- **A notification topic** numbers its messages consecutively. A subscription starts with a resync, delivers each message
  at most once, and resyncs, carrying the last sequence delivered, on a gap or a reconnect
  ([ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)
  rule 6).
- **A watch** on a key or prefix streams written, deleted and resync events in revision order. It replays from a
  revision only where the provider keeps history, and otherwise starts with a resync. After it applies every event up
  to a revision, the subscriber's view equals the store at that revision
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md) rule 4).
  Membership watches follow the same rule ([ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)
  rule 2).
- **Staleness is bounded.** A cache kept by notifications is stale for at most the time to the next notification or
  resync, never indefinitely (ADR-1005).

The RESP provider's earlier decisions constrain the answer. Every multi-step write is one Lua script
([ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)).
A keyspace, its revision counter and its channels share one slot
([ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md)).
StackExchange.Redis doesn't offer blocking commands on its multiplexer
([ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md)).

What the servers and the client offer (read 2026-11-25; sources under References):

- **Pub/Sub delivers at most once.** A message a subscriber can't receive, such as during a disconnect, is lost.
  Sharded Pub/Sub (`SPUBLISH`, `SSUBSCRIBE`) maps a channel to a slot like a key, and the cluster forwards its messages
  only within that slot's shard. Plain Pub/Sub (`PUBLISH`, `SUBSCRIBE`) reaches every node of a cluster.
- **Garnet** supports `PUBLISH`, `SUBSCRIBE`, `SPUBLISH` and `SSUBSCRIBE`, but not `SUNSUBSCRIBE`.
- **StackExchange.Redis** processes a subscription's messages in arrival order through its sequential queue. It raises
  `ConnectionFailed` and `ConnectionRestored` for a connection, and supports sharded channels.
- **Streams** keep entries that a reader fetches from an id, but StackExchange.Redis offers no blocking read, so a reader
  polls.
- **Keyspace notifications** report changes to keys, expiry included, only when the server's `notify-keyspace-events`
  setting enables them, which it doesn't by default. They carry no sequence and no payload, an expiry event can come
  well after the time to live ran out, and in a cluster each node reports only its own keys.
- **A key that expires by the server's own time to live** changes with no script running, so nothing numbers or
  announces the change.
- **Observed** in a test run on 2026-11-25 through StackExchange.Redis 3.4.35:
  - A script that increments a counter and publishes the new value in the same run delivered 1, 2 and 3 in order, over
    plain Pub/Sub on Valkey 9 and Garnet 2.2.14, and over sharded Pub/Sub on standalone Valkey 9.
  - Sharded Pub/Sub on standalone Garnet failed with `ERR This instance has cluster support disabled`.

## Decision

**The script that makes a change also takes its number and publishes it, in the same run. Subscribers check that
numbers arrive consecutively, and resync on any gap, reconnect or doubt. State that a watch can see expires through
scripts, never through the server's time to live, so every change is numbered.**

1. **Numbered in the writing script.** Publishing a notification is one script that increments the topic's sequence
   and publishes the sequence with the payload. A change to records or membership is one script that increments the
   keyspace's revision once and publishes one watch event on the keyspace's watch channel. The event carries that
   revision and every key the change wrote or deleted, up to the 64 of a transaction. A change and its number can't
   disagree, and each channel's numbers are consecutive.
2. **Channels in the keyspace's slot.** A topic's channel and a keyspace's watch channel carry the keyspace's hash tag
   (ADR-1024 rule 1). The profile chooses the kind: Valkey uses sharded Pub/Sub, which stays within the keyspace's
   shard, and Garnet uses plain Pub/Sub, since it has no `SUNSUBSCRIBE` and no sharded channels outside cluster mode.
3. **Starting a subscription.** The provider subscribes first, then reads the channel's current number and emits a
   resync carrying it. It drops messages numbered at or below it, so nothing is missed between the read and the first
   message.
4. **Checking each message.** A message numbered exactly one above the last delivered is delivered. Any other number,
   whether a gap or a number that went backwards after a failover, makes the subscription resync from the channel's
   current number. A watch delivers an event's keys that match its key or prefix, and advances its revision on every
   event, matching or not.
5. **Reconnects.** On `ConnectionFailed` for the subscription's connection, the subscription marks itself in doubt. On
   `ConnectionRestored` it resubscribes as needed and resyncs, as rule 3 does.
6. **A periodic check.** Every subscription reads its channel's current number at an interval, 5 seconds by default
   and set in the provider's options. When the number is ahead of the last delivered and stays ahead past a short grace
   for messages in flight, the subscription resyncs. This bounds how long a silently lost message can go unnoticed.
7. **Expiry through scripts.** A record with a time to live, a session and everything bound to a session store their
   deadline on the server's clock (`TIME` plus the duration), in an index kept in the keyspace's slot.
   - Every script on a keyspace treats state past its deadline as absent. It first removes up to a bounded number of
     expired entries, numbering and announcing their deletion as rule 1 does.
   - The periodic check of rule 6 and every session keep-alive run that removal, so expiry is announced within the
     check interval while anyone watches.
   - Each such key also gets a server time to live well past its deadline, one hour by default. That removes state no
     script ever revisits, in a keyspace that no one watches.
   - Occurrences, counters, claims and other state no watch can see keep ADR-1023 rule 5's server time to live.
8. **No history.** The provider keeps no history of changes, so every watch and notification subscription starts with a
   resync. It declares notifications and watches as at most once with gap detection.

ADR-1023 rule 5 is amended: a script still reads the current instant from `TIME`, but state a watch can see expires by
rule 7 here, and only other state expires by the server's time to live.

## Options considered

| Concern | Pub/Sub, numbered in the writing script | Streams, polled | Keyspace notifications | Polling the number only |
| --- | --- | --- | --- | --- |
| Gap detection | Consecutive numbers, checked per message and periodically | Entry ids; a reader catches up from its last id | None: no sequence | A changed number, with no detail |
| Latency | Push, as written | The poll interval | Push | The poll interval |
| History | None; starts with resync | Within the stream's trim limit | None | None |
| Expiry seen | Through scripts (rule 7) | Through scripts | Native, when enabled | Through scripts |
| Server settings | None | None | `notify-keyspace-events` on every node | None |
| Cost | One publish per change; one read per subscription per interval | A stream per channel kept trimmed; a poll per subscriber per interval | Events for every key in the server | A read per subscriber per interval |
| Fits ADR-1005 | Signals, no broker | Moves toward a log the product keeps | Signals | Signals |

- **Pub/Sub, numbered in the writing script.** Chosen: it delivers as fast as the server can, needs no server setting,
  and its numbers can't disagree with the changes they number. What it can lose, it detects and turns into a resync.
- **Streams, polled.** Rejected: replay would only shorten some resyncs, and it costs a stream per channel that someone
  must trim, plus polling, since StackExchange.Redis can't block on a read. That is the durable log ADR-1005 leaves to
  brokers. A future record may add a bounded replay window if resyncs prove expensive.
- **Keyspace notifications.** Rejected: they carry no sequence or payload, need a server setting that managed services
  may not expose, and in a cluster each node reports only its own keys.
- **Polling the number only.** Rejected as the main path, for its latency and load, but kept as rule 6's check.

## Consequences

- Every record and membership change in a keyspace publishes one message on the keyspace's watch channel, so a watch on
  one key also receives, and skips, events for the keyspace's other keys. A keyspace per tenant keeps that traffic
  small.
- Expiry of watchable state is visible to watchers within the check interval, and to reads at once.
- The scripts of ADR-1023 that touch watchable state begin with a bounded removal of expired entries, and their work
  stays bounded by that limit.
- A failover that loses acknowledged writes shows up as numbers going backwards, and subscribers resync instead of
  trusting a stale view.
- You chose wrong if resyncs dominate a subscriber's work under normal load, or if a consumer needs to resume a watch
  from a revision without rereading. Then a record that supersedes this one weighs a bounded replay window over Streams.

## References

- [ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md),
  [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md)
- [Valkey: Pub/Sub](https://valkey.io/topics/pubsub/), read 2026-11-25: delivery semantics and sharded Pub/Sub
- [Valkey: Keyspace notifications](https://valkey.io/topics/notifications/), read 2026-11-25: the setting that enables
  them, the delay of expiry events and events in a cluster
- [Garnet: API compatibility](https://microsoft.github.io/garnet/docs/commands/api-compatibility), read 2026-11-25:
  the Pub/Sub commands it supports
- StackExchange.Redis documentation, read 2026-11-25: [Pub/Sub Message Order](https://seredis.dev/PubSubOrder),
  [Events](https://seredis.dev/Events) and [Pipelines and Multiplexers](https://seredis.dev/PipelinesMultiplexers)
- A test run on 2026-11-25 with StackExchange.Redis 3.4.35 against `valkey/valkey:9` and `ghcr.io/microsoft/garnet`
  2.2.14 containers: a script that increments a counter and publishes it, over plain and sharded channels
