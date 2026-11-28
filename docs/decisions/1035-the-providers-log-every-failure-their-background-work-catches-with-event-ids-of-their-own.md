# 1035. The providers log every failure their background work catches, with event ids of their own

- **Status:** Proposed
- **Date:** 2026-11-28
- **Deciders:** NCode Group

## Context

Each provider runs work no caller waits on, and that work catches the failures it can survive and goes on:

- **PostgreSQL.** A sweep removes expired rows in passes and steps; a pump reads a log for its subscribers when a
  wake-up or a periodic check asks; a listening connection holds `LISTEN` for the keyspaces the subscriptions need and
  connects again when it breaks
  ([ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
  rule 7,
  [ADR-1034](1034-postgresql-notifications-and-watches-are-numbered-in-the-writing-transaction-kept-in-a-bounded-log-and-woken-by-notify.md)
  rules 3 and 8).
- **RESP.** A subscription reads its channel's number at each check and at each resync; a record watch reads each
  written record, and a membership watch reads its group's view, to build the events they deliver; a subscription
  unsubscribes as it ends
  ([ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md),
  [ADR-1029](1029-resp-watches-read-each-written-record-from-the-table-and-membership-events-name-their-group.md)).

A failure in that work is caught, since the next round repeats the work or the subscriber resyncs
([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
gives the failures their codes). Unless the failure is logged, a sweep that fails every pass, a pump that never reads
its log or a subscription that never reads its channel's number looks, to an operator, like a quiet system whose
subscribers resync often and whose expired rows linger.

Logging is a change consumers observe: it adds entries to their logs, with categories, levels and event ids they can
filter on, so it is decided before it is built (project-template
[ADR-0010](project-template/0010-a-change-consumers-can-observe-is-decided-before-it-is-built.md)). The decisions that
bound it already exist:

- The hosting package's event ids are 2000 to 2999, leaving 1000 to 1999 to `NCode.Coordination`; no message carries a
  keyspace, a key or a segment, since they can name a tenant; and a loop that meets an exception no call it guards
  expects logs it as an error
  ([ADR-1021](1021-a-host-holds-one-session-for-its-lifetime-joins-its-membership-with-it-and-closes-it-as-it-stops.md)
  rules 12 and 13).
- A host decides where its logs go; a library logs through `ILogger` and registers no logging provider (project-template
  [ADR-0115](project-template/0115-a-host-logs-to-standard-output.md) rule 5), and no entry carries a secret
  (project-template
  [ADR-0146](project-template/0146-secrets-are-never-logged-compared-in-constant-time-and-shown-once.md)); a
  PostgreSQL connection string holds one.
- Microsoft's guidance for library authors (read 2026-11-28; sources under References) recommends source-generated
  logging methods, which keep message templates, ids and levels out of the calling code, and a logger per class whose
  category is the class's name, created from an `ILoggerFactory` where a type builds helpers that log and taken as an
  `ILogger<T>` where one class logs for itself.

## Decision

**Each provider logs every failure its background work catches and goes on from, once per failed round, with an event
id from a band of its own, through the `ILogger` its host's `ILoggerFactory` makes. The work behaves as it does without
the entry: the failure stays caught, and the next round repeats the work or the subscriber resyncs.**

1. **What is logged.** Every failure the providers' background work catches and goes on from.
   - PostgreSQL: a failed sweep, a failed sweep pass, a failed sweep step, a failed pump of a log (on a wake-up or on a
     periodic check), a failed listening connection, and a failed disposal of a broken listening connection.
   - RESP: a subscription's failed read of its channel's number (at a check or at a resync), a record watch's failed
     read of a written record, a membership watch's failed read of its group, and a failed unsubscribe as a
     subscription ends.
2. **Event ids.** Each provider owns a band: RESP 3000 to 3999 and PostgreSQL 4000 to 4999, beside
   `NCode.Coordination`'s 1000 to 1999 and the hosting's 2000 to 2999 (ADR-1021 rule 12). A provider names its ids as
   constants `Base + n` in its `Logging/EventIds.cs`, and only appends: an id is never reused or renumbered.

   | Id | Name | Level | A failure of |
   | --- | --- | --- | --- |
   | 3001 | `ChannelNumberReadFailed` | Warning | A subscription's read of its channel's number |
   | 3002 | `WatchRecordReadFailed` | Warning | A record watch's read of a written record |
   | 3003 | `WatchGroupReadFailed` | Warning | A membership watch's read of its group |
   | 3004 | `UnsubscribeFailed` | Debug | The unsubscribe as a subscription ends |
   | 4001 | `SweepFailed` | Error | A whole sweep, by an exception the sweep doesn't expect |
   | 4002 | `SweepPassFailed` | Warning | One pass of the sweep |
   | 4003 | `SweepStepFailed` | Warning | One step of the sweep |
   | 4004 | `PumpFailed` | Warning | A pump's read of a log, by a coded failure or a missing keyspace |
   | 4005 | `PumpFaulted` | Error | A pump's read of a log, by an exception the pump doesn't expect |
   | 4006 | `ListenerConnectionFailed` | Warning | The listening connection |
   | 4007 | `ListenerDisposeFailed` | Debug | The disposal of a broken listening connection |
3. **Levels.** `Warning` for a failure the next round repeats: a `CoordinationException`, or PostgreSQL's missing
   keyspace. `Error` for an exception the code doesn't expect (4001, 4005), as ADR-1021 rule 13 logs a faulted loop.
   `Debug` for a failure at teardown that leaves nothing to do (3004, 4007).
4. **Messages.** Each message is a source-generated `[LoggerMessage]` extension method in the provider's internal
   static partial `Logging/Log.cs`. No message carries a keyspace, key, segment, channel, topic or connection string,
   since they can name a tenant or hold a secret (ADR-1021 rule 12; project-template ADR-0146). The caught exception is
   the entry's exception. A sweep entry names its pass or step by a fixed name of the code's, never by a name from data.
5. **Where loggers come from.** The coordination builder's provider factory resolves `ILoggerFactory` from the host's
   `IServiceProvider`, and uses `NullLoggerFactory.Instance` when the host registered none. A component that logs takes
   an `ILogger<T>`, its category its own type; a component that builds others that log takes the `ILoggerFactory`.
   The providers register no logging provider (project-template ADR-0115 rule 5), and no public member changes.

## Options considered

| Concern | Source-generated entries through `ILogger` | No logging | An event or a callback in the options | A metric of caught failures |
| --- | --- | --- | --- | --- |
| A repeating failure is visible | Yes, once per failed round | No | Yes, where the host subscribes | As a count, with no cause |
| The host's own tooling applies | Yes: categories, levels, filters, sinks | Not applicable | No: the host wires a second channel | Yes, for metrics |
| Carries the exception | Yes | No | Yes | No |
| Public API | None | None | A member of the options in each provider | A meter, which is public |
| Cost when unused | The enabled check of a source-generated method | None | A null check | A counter |

- **Source-generated entries through `ILogger`.** Chosen: every host already configures logging, the entry carries the
  exception, and a host that wants silence filters a category or registers no provider. It adds no public member.
- **No logging.** Rejected: a failure that repeats stays invisible until a symptom, such as lingering rows or constant
  resyncs, sends an operator looking.
- **An event or a callback in the options.** Rejected: it is a second channel beside the logging every host has, and
  every host would have to wire it to reach its logs.
- **A metric of caught failures.** Worth adding beside the log, and a separate decision, since a metric has its own
  owner and bounded dimensions (project-template
  [ADR-0113](project-template/0113-metrics-have-one-owner-per-signal-and-bounded-dimensions.md)).

## Consequences

- A failed round logs once, and a round is a pass, a step, a read or an attempt, so an outage logs at these rates:
  - The PostgreSQL sweep logs one entry for each of its twelve passes and two steps that fail, at every
    `SweepInterval`: 4002 and 4003, up to fourteen entries, and 4001 only for an unexpected failure.
  - A log's pump logs once for each failed read, and a wake-up, a check or a reconnect starts a read, so a burst of
    wake-ups logs a burst; coalescing leaves at most one waiting pump per log.
  - The listening connection logs once for each failed connection attempt, a second apart (`ReconnectDelay`), until
    the server returns.
  - RESP's subscriptions log once for each failed read, at a check of each subscription.

  A host that finds these too many filters the category or the ids, as the last bullet here says.
- An operator can tell a provider that can't reach its server from a quiet one, by the entries of 3001 to 3003 or 4001
  to 4006, and a defect from an outage, by the level.
- The messages name no keyspace, key or channel, so an entry shows that something failed, not for whom; the exception's
  own message is the provider's or the driver's, and a host that logs exceptions with their data sees what they hold.
- Each component that logs takes a logger as its last constructor parameter, so construction in tests supplies one, a
  null logger where the entry doesn't matter.
- A later id joins its band by appending; a provider that outgrows 1,000 ids needs a record that moves the bands.
- You chose wrong if hosts report logs from the providers that they can't act on, or the entries repeat so often that
  hosts filter the whole category. Then a record that supersedes this one weighs logging a failure's first occurrence
  and its end, or a metric in its place.

## References

- [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  [ADR-1021](1021-a-host-holds-one-session-for-its-lifetime-joins-its-membership-with-it-and-closes-it-as-it-stops.md),
  [ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md),
  [ADR-1032](1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md),
  [ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
  and
  [ADR-1034](1034-postgresql-notifications-and-watches-are-numbered-in-the-writing-transaction-kept-in-a-bounded-log-and-woken-by-notify.md),
  the PostgreSQL provider's, and
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md) to
  [ADR-1030](1030-the-resp-providers-runs-drop-its-own-connections-restart-and-fail-over-servers-of-their-own-and-repeat-every-suite-on-clusters.md),
  the RESP provider's
- Project-template
  [ADR-0010](project-template/0010-a-change-consumers-can-observe-is-decided-before-it-is-built.md),
  [ADR-0113](project-template/0113-metrics-have-one-owner-per-signal-and-bounded-dimensions.md),
  [ADR-0115](project-template/0115-a-host-logs-to-standard-output.md) and
  [ADR-0146](project-template/0146-secrets-are-never-logged-compared-in-constant-time-and-shown-once.md)
- Microsoft Learn, read 2026-11-28, public documentation only:
  [Logging guidance for .NET library authors](https://learn.microsoft.com/dotnet/core/extensions/logging/library-guidance)
  and [Compile-time logging source generation](https://learn.microsoft.com/dotnet/core/extensions/logging/source-generation),
  on source-generated logging methods, a logger per class and where a library's loggers come from
