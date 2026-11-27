# 1029. RESP watches read each written record from the table, and membership events name their group

- **Status:** Proposed
- **Date:** 2026-11-26
- **Deciders:** NCode Group
- **Amends:** 1025, 1028

## Context

[ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
numbers every change in the script that makes it, publishes it over Pub/Sub and resyncs a subscriber on any doubt.
[ADR-1027](1027-resp-records-live-in-one-table-per-keyspace-indexed-by-digests-of-their-segments.md) rule 5 makes a
keyspace's event its revision and a `w` or `d` line per record written or deleted, and
[ADR-1028](1028-resp-ownership-state-lives-in-fixed-keys-per-keyspace-bound-to-sessions-through-one-index.md) rule 5
publishes a membership-only change as the revision alone. Subscriptions built on them meet these forces:

- **F1. What a resync carries.** ADR-1025 rule 3 reads the channel's number and emits a resync carrying it, while a
  notification's resync carries the last sequence delivered, or 0 when none
  ([ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)
  rule 6), which the conformance kit checks. A watch's resync carries the revision after which the subscriber applies
  changes ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)
  rule 4).
- **F2. A written record's value.** An event names the records a change wrote, not their values, each up to 65,536
  bytes, while a watch's written change carries the record.
- **F3. A membership watch.** A membership-only event matches no record, so a membership watch can't tell a change to
  its group from any other change in its keyspace without listing the group.
- **F4. Message order.** StackExchange.Redis hands a `ChannelMessageQueue`'s messages over in arrival order, while
  the handlers given to `Subscribe` may run concurrently, in no order (read 2026-11-26).
- **F5. Connection events.** StackExchange.Redis raises `ConnectionFailed` once for a connection until it is
  restored, then `ConnectionRestored` (read 2026-11-26).
- **F6. Commands.** Garnet's `redis.acl_check_cmd` rejects `SUNSUBSCRIBE` as an unknown command, and Garnet refuses
  `SPUBLISH` from a script; Valkey accepts both (test run, 2026-11-26).

## Decision

**Each subscription reads its channel through one ordered queue and a periodic check. A record watch reads each
written record from the table and delivers it only at the event's revision. A membership change names its group in
the keyspace's event, and a membership watch lists its group only when an event names it or its channel resyncs.**

1. **One ordered queue.** A subscription is one `ChannelMessageQueue`, on a sharded channel when the profile has them
   and a plain one otherwise. Its messages, a timer's ticks and every connection's `ConnectionFailed` and
   `ConnectionRestored` go, in order, into one queue of at most 1,024 signals. A signal that finds the queue full is
   dropped, and the next message's number or the next check finds the gap.
2. **Reading the numbers.** A message's number is the decimal digits before its first line feed. A subscription reads
   its channel's number when it starts and on every resync, and drops a message at or below it until it delivers
   one. It delivers the message numbered one above the last, and resyncs on any other number, a message without one,
   and a restored connection. A failed connection, a failed read of the number and a failed read of what a message
   names put it in doubt, and the next check resyncs it. A failure to subscribe or to read the first number fails
   the subscription; after that, the stream ends only when its caller cancels.
3. **The check.** Every `RespProviderOptions.CheckInterval`, 5 seconds by default, at least a millisecond, the
   shortest period a timer takes, and less than the hour of ADR-1027 rule 6's backstop, a subscription reads its
   channel's number. A number below the last delivered, or any number while in doubt, resyncs. A number above it waits a grace of 1 second, handles what arrived
   meanwhile, and resyncs unless the messages reached it; a check during the grace is skipped. Reading a keyspace's
   revision runs its expiry removal, so an expiry reaches a watch within the interval.
4. **Notifications.** A topic's sequence is the key `{keyspace}:topic:<name>`, which never expires, and its channel
   has the same name. One script increments the sequence and publishes it, a line feed and the payload. A resync
   carries the last sequence the subscription delivered, or 0 (F1); the number it read stays the subscription's own.
   This amends ADR-1025 rule 3.
5. **Record watches.** A watch's resync carries the keyspace's revision it read. For each `w` line whose key the watch
   covers, it reads the record and delivers it when the record's revision is the event's; a later revision or an
   absent record means a later event carries the change, so the watch skips an intermediate value, as ADR-1014 rule 4
   allows. A `d` line is a delete at the event's revision. A prefix watch covers the keys a listing of the prefix
   returns. The provider keeps no history, so a revision to resume after is not used.
6. **Membership events.** A join, a leave and a session's end that removes a member add a line `m` and the group's
   member to the keyspace's event, and record watches skip it. This amends ADR-1028 rule 5.
7. **Membership watches.** A membership watch subscribes to its keyspace's watch channel and lists its group when it
   starts, on an event that names the group and on every resync, yielding a view whose revision is greater than the
   last. Waits and an election's watch read the server every 100 ms, since a grant publishes nothing. This amends
   ADR-1028 rule 8.
8. **The probe.** The startup probe checks `SSUBSCRIBE` and `SUNSUBSCRIBE` on a profile with sharded channels, and
   `SUBSCRIBE` and `UNSUBSCRIBE` otherwise (F6).
9. **Declarations.** Record watches keep `consistency.linearizable-until-failover` and
   `delivery.at-most-once-with-gap-detection`, and notifications keep `delivery.at-most-once-with-gap-detection`, as
   ADR-1025 rule 8 says.

## Options considered

- **Values in each event.** Rejected: a transaction's event would carry up to 64 values of 65,536 bytes to every
  subscriber of its keyspace.
- **Delivering whatever a read finds.** Rejected: a later write's value would arrive at an earlier event's revision,
  out of revision order.
- **Listing the group on every event.** Rejected: every record change in a keyspace would cost every membership
  watch in it a listing.
- **A handler per subscription.** Rejected: its messages may run out of order (F4), and each reordering would resync.
- **An unbounded queue.** Rejected: a subscriber that falls behind would hold every message in memory; a bounded one
  turns falling behind into a resync, as the in-process provider does.
- **Only the subscription connection's events.** Rejected: which connection carries a channel depends on the protocol
  and the topology, and a needless resync costs one read.

## Consequences

- A written change costs a record read for each key a watch covers, and a busy key's intermediate writes are skipped.
- A membership watch lists its group only when the group changes, a resync happens or it starts.
- While anyone watches, an expiry is announced within the interval, and a lost message noticed within the interval
  and the grace.
- A failover that loses writes moves numbers backwards: a subscription resyncs, and the numbers it delivers after it
  may repeat ones it delivered before.
- You chose wrong if record reads dominate a watch's cost, or resyncs from a full queue are common; then a record that
  supersedes this one weighs values in events or a larger queue.

## References

- [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md),
  [ADR-1027](1027-resp-records-live-in-one-table-per-keyspace-indexed-by-digests-of-their-segments.md),
  [ADR-1028](1028-resp-ownership-state-lives-in-fixed-keys-per-keyspace-bound-to-sessions-through-one-index.md)
- StackExchange.Redis documentation, read 2026-11-26: [Pub/Sub Message Order](https://seredis.dev/PubSubOrder) and
  [Events](https://seredis.dev/Events), and the XML documentation file of StackExchange.Redis 3.4.35
- Microsoft Learn, read 2026-11-26:
  [TimeProvider.CreateTimer](https://learn.microsoft.com/dotnet/api/system.timeprovider.createtimer),
  [Task.Delay](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.delay) and
  [BoundedChannelFullMode](https://learn.microsoft.com/dotnet/api/system.threading.channels.boundedchannelfullmode)
- A test run on 2026-11-26 through `valkey-cli` against `valkey/valkey:9.2.0` and `ghcr.io/microsoft/garnet:2.2.13`
  started with `--lua --lua-transaction-mode true`: the notification script, `SPUBLISH` from a Garnet script, and
  `redis.acl_check_cmd` for the subscribe commands
- The notification, record watch and membership suites of NCode.Coordination.Testing, run against both servers
