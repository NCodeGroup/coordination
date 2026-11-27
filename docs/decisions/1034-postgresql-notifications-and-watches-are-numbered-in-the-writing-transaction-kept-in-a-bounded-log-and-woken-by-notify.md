# 1034. PostgreSQL notifications and watches are numbered in the writing transaction, kept in a bounded log and woken by NOTIFY

- **Status:** Proposed
- **Date:** 2026-11-27
- **Deciders:** NCode Group

## Context

Notifications and watches have to tell a subscriber when it may have missed something:

- **A notification topic** numbers its messages consecutively. A subscription starts with a resync, delivers each
  message at most once in sequence order, and resyncs whenever the subscriber may have missed one
  ([ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)
  rule 6). The conformance kit's checker accepts consecutive numbers, or a resync between two that aren't.
- **A watch** on a key or prefix streams written, deleted and resync events in revision order. It starts from an
  optional revision and replays the changes after it when the provider keeps them, and otherwise starts with a resync.
  After the subscriber applies every event up to a revision, its view equals the store at that revision
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md) rule 4).
  Membership watches follow the same rule.
- **Staleness is bounded**, never indefinite (ADR-1005).

The PostgreSQL provider's earlier decisions constrain the answer. A change a watch can see is one transaction that
locks its keyspace's row and takes its next revision, so revisions follow commit order
([ADR-1032](1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md)
rule 3). State lives in shared tables, with a sweep that removes expired rows
([ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)).
The provider listens on one connection held open
([ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md) rule 5).

What PostgreSQL offers (read 2026-11-27; sources under References):

- **`NOTIFY` and `pg_notify`** send a notification to every session that has run `LISTEN` on its channel in the same
  database. A notification sent in a transaction is delivered only if it commits, and notifications from different
  transactions arrive in commit order. Identical notifications in one transaction are folded into one. A payload must be
  shorter than 8,000 bytes by default. A session that isn't listening, or whose connection is down, never receives what
  was sent meanwhile. Undelivered notifications wait in a queue of 8 GB by default, and a full queue fails the
  notifying transaction at commit. A hot standby refuses `LISTEN` and `NOTIFY`, and the queue isn't replicated.
- **PostgreSQL 19** wakes only the sessions listening on a notified channel, where earlier versions woke most sessions.
- **A server's start time**, `pg_postmaster_start_time()`, changes each time the server starts, and any role can read
  it. A standby promoted after a failure is another server, with its own start time.
- **Logical decoding** streams committed changes from the write-ahead log through a replication slot. It needs
  `wal_level = logical`, a role with the `REPLICATION` attribute or a managed service's equivalent, and a slot that
  keeps the log on disk until its consumer reads it.
- **Observed** in a test run on 2026-11-27 against `postgres:18` (18.7): two transactions that each locked a keyspace's
  row, took its next revision and called `pg_notify` with it were received by a listening session as `1` then `2`,
  although the second started before the first committed; a payload of 8,000 bytes failed with `22023`; and a role
  that isn't a superuser read `pg_postmaster_start_time()`.

## Decision

**The transaction that makes a change numbers it and writes it to a log in the database, beside the state it changes.
The same transaction calls `pg_notify` to wake the keyspace's listeners, who read the log after the last number each
delivered. A subscriber resyncs on a reconnect, and when the log no longer holds what it needs or its numbers went
backwards.**

1. **Numbered in the writing transaction.** Publishing a notification locks its topic's row, takes the topic's next
   sequence number and writes the message, with its payload of at most 4,096 bytes, to the message log. A change a
   watch can see takes its keyspace's next revision (ADR-1032 rule 3) and writes one entry to the change log, naming
   every key it wrote or deleted, up to the 64 of a transaction, with each written record's value and lifetime, the
   group of a membership change, or the session a session's end ended
   ([ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
   rule 6). A number and its change commit together or not at all, and since each keyspace's and
   topic's row is held to commit, a log never gains an entry below one already visible.
2. **Woken by `pg_notify`.** The same transaction calls `pg_notify` once on its keyspace's channel, named from the
   schema and the keyspace's id within 63 bytes, with a payload that says which log grew and to what number. The payload
   carries no state, so its size limit never binds.
3. **One listening connection.** Each provider instance runs `LISTEN` on the channel of every keyspace one of its
   subscriptions needs, on the connection of ADR-1031 rule 5, and `UNLISTEN` once the keyspace's last subscription
   ends. A wake-up makes the instance read each grown log after the lowest number its subscriptions need, once for all
   of them, and deliver in order.
4. **Starting.** A notification subscription listens first, reads its topic's current number as its cursor and emits a
   resync carrying the last sequence delivered, 0 at the start, as ADR-1015 rule 6 says, then delivers what the log
   holds above its cursor. The cursor is internal; every resync carries the last sequence delivered, as
   [ADR-1029](1029-resp-watches-read-each-written-record-from-the-table-and-membership-events-name-their-group.md)
   does for the RESP provider. A watch given a revision the log still holds replays the
   changes after it. A watch given none, or one the log no longer holds, listens first, then emits a resync at the
   keyspace's current revision, the revision after which the subscriber applies changes.
5. **Delivering.** A watch delivers the keys of each change that match its key or prefix, with what the change wrote,
   and advances its revision on every change, matching or not. A change that removes, replaces or rebinds a row bound to
   an ended session names that session in its entry. For an entry that ends a session, a watch reads, in one snapshot
   with the log and in bounded pages, the keys it covers that are still bound to that session and those that later
   entries name as removing or replacing its bindings, and delivers their deletion at the entry's revision; the sweep
   keeps the bound rows until the entry leaves the log (ADR-1033 rule 7). When that snapshot no longer holds the entry,
   the watch resyncs. A membership watch reads its group's view when it starts, on every resync, and when an entry names
   its group or ends a session, and emits a view only at a revision greater than the last it emitted, labeled with the
   revision it read it at. Each message and change is delivered at most once.
6. **Bounded retention.** Each log entry keeps a deadline on the server's clock, 5 minutes after it is written by
   default and set in the provider's options, and the sweep of ADR-1033 rule 7 removes it in bounded batches. The
   keyspace's and topic's rows record the highest number removed, raised in the same transaction that removes the
   entries, so a reader knows when the log no longer holds the entry after its last. A reader reads that number, the
   current number and the entries after its cursor in one snapshot, so the sweep can't remove an entry between them.
7. **The server's identity, reconnects and resyncs.** The provider connects only to a primary with `fsync` on
   ([ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md) rules 6 and 8),
   and a primary loses none of its own commits without restarting, so the server's start time,
   `pg_postmaster_start_time()`, identifies a history that only grows. The listening connection reads it each time it
   connects.
   - A reconnect resyncs every subscription and watch, as the contracts say of a reconnect, after it listens again.
   - A subscription also resyncs when the log no longer holds the entry after its cursor, or when the current number is
     below its cursor.
   - A watch given a revision replays from the log only while the instance has seen one server identity; once it has
     seen another, as after a failover, where a promoted standby may have lost changes and taken new ones under the same
     numbers, such a watch starts with a resync, and the provider drops its cached keyspace ids.
8. **A periodic check.** Every subscription's current number is read at an interval, 5 seconds by default and set in
   the provider's options. A number ahead of the last delivered makes the instance read the log, and one behind makes
   the subscription resync. This bounds how long a wake-up lost without a failed connection can go unnoticed.
9. **Declared delivery.** The provider declares notifications and watches as `delivery.at-most-once-with-gap-detection`:
   a gap the log fills is never seen, and one it can't is a resync.

## Options considered

| Concern | A log, woken by `pg_notify` | `pg_notify` carrying each event | Polling a log | Logical decoding |
| --- | --- | --- | --- | --- |
| Gap detection | Consecutive numbers, read from the log | Numbers in payloads, checked per message | Numbers, read from the log | Log positions |
| Latency | Push, then one read | Push | The poll interval | Push |
| History | The retention window, so a watch from a revision replays | None | The retention window | Whatever the slot keeps |
| Payload size | Unbounded by `NOTIFY`; values up to the contracts' limits | Under 8,000 bytes, below a record's 65,536 | Unbounded | Unbounded |
| Server settings and privileges | None | None | None | `wal_level = logical`, a replication role, a slot per consumer |
| Cost | A log row per change, swept; a read per wake-up per instance | A notification per change | A read per subscription per interval | Disk held for an absent consumer |
| Fits ADR-1005 | Signals, a short window, no broker | Signals | Signals | Moves toward a durable log |

- **A log, woken by `pg_notify`.** Chosen: it delivers as fast as the server commits, needs no setting or privilege
  beyond the provider's tables, and lets a watch from a revision replay instead of resyncing. Its numbers
  can't disagree with the changes they number, and what it can lose, past its window or in a failover, it detects and
  turns into a resync. Its cost is a log row per change, which the sweep keeps to the retention window.
- **`pg_notify` carrying each event.** Rejected: a record's value can be larger than a notification's payload, and a
  watch given a revision couldn't replay.
- **Polling a log.** Rejected as the main path, for its latency and its load, but kept as rule 8's check.
- **Logical decoding.** Rejected: it needs a server setting, a replication privilege that managed services grant
  differently or not at all, and a slot that fills the server's disk when a consumer goes away.
- **Triggers that notify.** Rejected: they put the provider's logic on the server, which ADR-1032 rule 11 rules out.

## Consequences

- Every change a watch can see writes one log row and wakes its keyspace's listeners, so a watch on one key also
  wakes for, and skips, the keyspace's other changes. A keyspace per tenant keeps that traffic small.
- Expiry a watch can see is announced when a read, a write or the sweep removes it, within a sweep interval while any
  provider instance runs.
- A cache can list, then watch from the listing's revision, and the watch replays what changed between the two.
- A failover resyncs every subscription and watch, through the reconnect and the server's identity, so a promoted
  standby that lost changes and took others under the same numbers can't leave a subscriber with a diverged view.
- A revision carried from before a failover the instance never saw, such as one another process read, can replay a
  diverged history; the declared consistency, linearizable until failover, already gives up that case.
- Every notifying commit adds to PostgreSQL's notification queue, so the provider's benchmark measures how many
  notifying changes per second a database sustains.
- You chose wrong if the log's rows or the wake-ups measurably load a consumer's database, or if resyncs dominate a
  subscriber's work under normal load. Then a record that supersedes this one weighs a longer window, coarser
  wake-ups or logical decoding.

## References

- [ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
  and [ADR-1029](1029-resp-watches-read-each-written-record-from-the-table-and-membership-events-name-their-group.md),
  the RESP provider's counterparts,
  [ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md),
  [ADR-1032](1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md),
  [ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
- `src/NCode.Coordination.Abstractions/Notifications/INotificationService.cs` and
  `src/NCode.Coordination.Abstractions/Records/IRecordWatchService.cs`: when a stream resyncs and when it replays, and
  `src/NCode.Coordination.Testing/Checking/NotificationDeliveryChecker.cs`: what the kit accepts
- PostgreSQL 19 documentation, read 2026-11-27: [NOTIFY](https://www.postgresql.org/docs/19/sql-notify.html),
  [LISTEN](https://www.postgresql.org/docs/19/sql-listen.html),
  [Hot Standby](https://www.postgresql.org/docs/19/hot-standby.html),
  [Logical decoding](https://www.postgresql.org/docs/19/logicaldecoding.html),
  [System information functions](https://www.postgresql.org/docs/19/functions-info.html), and the
  [PostgreSQL 19 release notes](https://www.postgresql.org/docs/19/release-19.html), on the sessions `NOTIFY` wakes
- Npgsql documentation, read 2026-11-27: [Waiting for notifications](https://www.npgsql.org/doc/wait.html) and
  [Keepalive](https://www.npgsql.org/doc/keepalive.html)
- A test run on 2026-11-27 with `psql` against a `postgres:18` (18.7) container: two transactions numbering and
  notifying under one keyspace row, received by a listening session, `pg_notify` with payloads of 7,999 and 8,000
  bytes, and `pg_postmaster_start_time()` read by a role that isn't a superuser
