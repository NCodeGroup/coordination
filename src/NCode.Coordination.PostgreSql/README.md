# NCode.Coordination.PostgreSql

The PostgreSQL provider of [NCode.Coordination](https://github.com/NCodeGroup/coordination): coordination over
PostgreSQL 15 or later, through Npgsql, in tables of one schema that it migrates.

## Adding it

```csharp
services.AddCoordination(coordination => coordination
    .AddPostgreSqlProvider(postgresql => postgresql.ConnectionString =
        "Host=db.internal;Database=app;Username=coordination;Password=...;SSL Mode=VerifyFull")
    .Bind(new ProviderBinding(PostgreSqlCoordinationBuilderExtensions.ProviderName)));
```

Set `ConnectionString`, an Npgsql connection string, and the provider builds its own data source and disposes it with
the provider; or set `DataSource` to a factory that returns the host's own `NpgsqlDataSource`, which the provider uses
and never disposes. Exactly one of the two is set.

## Connections

- Every connection reaches the primary. A connection string that names several hosts, and a host's data source that
  does, are filtered to the primary, so a write never reaches a standby.
- The provider holds one connection for listening. It must reach PostgreSQL directly or through session pooling, never
  transaction pooling, which doesn't keep a session's `LISTEN`.
- `Multiplexing` must be off.
- A host's data source must set `Keep Alive`, which the probe checks, as it checks `Multiplexing`; the listening
  connection needs it to notice a server that went away.
- No connection enlists in an ambient `TransactionScope`: the provider opens each one outside any ambient
  transaction, and a connection string it builds turns `Enlist` off.
- A connection string it builds keeps its `Keep Alive` setting, or sets 15 seconds when the string leaves it at 0.

## The role

With `ApplyMigrations` on, the default, the role creates the schema, or owns it. Otherwise the role needs `USAGE` on
the schema and `SELECT`, `INSERT`, `UPDATE` and `DELETE` on its tables. The role is never a superuser, and the probe
refuses a role that lacks a privilege it needs.

## Profiles

`Profile` names the deployment, `PostgreSqlProfiles.Asynchronous` by default. When the host starts, the provider
checks what it can of the server and fails the start otherwise. The profile declares the durability the deployment
must make true, and the host chooses the one that its deployment satisfies.

- **Standalone** (`postgresql-standalone`) declares `durability.durable`, for one server whose commits reach disk.
  It needs `fsync` on and `synchronous_commit` not `off` for the provider's role.
- **Asynchronous** (`postgresql-asynchronous`) declares `durability.replicated-asynchronously`, for a primary whose
  standbys copy commits after it acknowledges them, so a failover may lose commits. It needs the same settings as
  Standalone, and is the default because its declaration is true of the most deployments.
- **Synchronous** (`postgresql-synchronous`) declares `durability.durable`, for a primary that waits for a standby
  before it acknowledges a commit. It also needs a standby in `synchronous_standby_names` and `synchronous_commit` set
  to `on` or `remote_apply`, set for the provider's role.

A service whose replication the probe can't see takes a profile of the host's own: `new PostgreSqlProfile(name,
durability, requiresSynchronousStandby)`. The probe still checks `fsync` and `synchronous_commit`.

## The schema

`Schema` names the schema that holds the tables, `coordination` by default: 1 to 63 lowercase letters, digits and
underscores, starting with a letter or an underscore.

With `ApplyMigrations` on, the provider applies each missing migration at start, each in its own transaction under an
advisory lock, so hosts that start together don't race. A host that applies schema changes itself sets
`ApplyMigrations = false` and runs the scripts of `PostgreSqlSchema.GetMigrations(schema)` in order, each once, in its
own transaction. The probe refuses a schema it can't use: one that is missing, or whose version this release can't
work with.

## Keyspaces

A keyspace is a row whose lock orders the changes that watches can see, so the changes of one keyspace are numbered
one after another and those of different keyspaces don't wait for each other. A keyspace per tenant is the unit of
scale.

## Primitives

The provider keeps records, record watches, transactions, occurrences, sets, counters, sequences, sessions,
membership, leases, semaphores, elections, claims and notifications in tables of its schema, and every time to live is
measured on the server's clock.

- **Occurrences, counters, sequences, sets and claims**: one statement or one transaction each, and none takes a
  keyspace's row, so none waits for the writers of a keyspace. A batch of counter increments locks its counters in key
  order, so two batches never wait for each other in a cycle. A count that passes `bigint` fails as `limit-exceeded`
  and is not applied. A set holds at most 10,000 live members.
- **Records and transactions**: a change locks its keyspace's row, takes the keyspace's next revision and logs the keys
  it wrote or deleted, in the same database transaction. A listing returns pages of digests, and its continuation token
  is under 700 characters.
- **Sessions**: a session's end, by a close, by a write that meets it expired or by the sweep, is one change in its
  keyspace's log. The records, members and grants bound to it are absent from then on, and the sweep deletes them once
  the session's entry has left the log.
- **Membership**: a join, a leave and a session's end are changes of the keyspace, and a group's watch reads them from
  its keyspace's change log.
- **Leases, semaphores and elections**: each grant carries a fencing token from a row per resource that outlives every
  grant, so a grant after an expiry or a sweep still gets a greater one. A grant locks its keyspace's row but takes no
  revision. Waiting for a release or a session's end, and watching a role, read the server every 100 milliseconds until
  the condition holds or the caller cancels.
- **Notifications**: a publish takes the topic's next sequence and stores the message, up to 4,096 bytes, in the topic's
  log, and wakes the subscribers with `pg_notify`.
- **Record and membership watches**: a watch reads its keyspace's change log after the revision it has delivered. It
  replays after a revision the log still holds, and starts with a resync when the log no longer holds it.
- **Resyncs**: every watch and subscription reads its log in order and resyncs after a gap in the numbers, a lost and
  restored listening connection, a log that dropped an entry the subscriber still needs, a server whose start time
  differs from the one before the reconnect, or a log whose number is behind a subscriber's. Every `CheckInterval`, 5
  seconds by default, each log is also read without a wake-up, so a lost `pg_notify` costs at most that long.
- **The sweep**: every `SweepInterval`, once the probe has passed, the provider deletes the expired rows, in
  statements of at most `SweepBatchSize` rows, each repeated while it deletes a full batch, that skip rows another
  transaction holds, and ends the expired sessions.
  It also deletes the entries of change logs and topic logs older than `LogRetention`, 5 minutes by default, at least a
  second and at most a day, and a watch or subscription that needs a deleted entry resyncs.

The provider declares every primitive but notifications linearizable until the server fails over, since an old primary
goes on answering reads after a standby is promoted. It declares the profile's durability for every primitive but
record watches and notifications, which keep only a bounded log. Transactions and counter batches are atomic across
partitions, because one database transaction commits every row. Record watches and notifications are at most once with
gap detection: a message can be lost, but never without a resync after it. Every primitive but sequences, record
watches and notifications declares the server's clock.

## Timeouts

`StatementTimeout`, `LockTimeout` and `IdleInTransactionTimeout`, 5 seconds each by default, are set with `SET LOCAL`
in every operation's transaction, so they bind that operation and not the connection that the pool keeps. Each is at
least a millisecond and less than an hour. A call that exceeds one fails as `unavailable`, and says whether it may have
taken effect.

The provider takes its host's `TimeProvider` for the time between its sweeps, its log checks, its polls and its
listening connection's reconnects, and the system clock when the host registered none. Every deadline of a stored row
stays on the server's clock.

## Logging

The provider logs through the `ILogger` its host's `ILoggerFactory` makes, with a category per class, and uses no
logger when the host registers no `ILoggerFactory`. It registers no logging provider: the host decides where its logs go.
It logs each failure its background work catches and goes on from, once per failed round, with an event id of its band,
4000 to 4999:

| Id | Name | Level | Meaning |
| --- | --- | --- | --- |
| 4001 | `SweepFailed` | Error | A whole sweep failed unexpectedly; the next sweep runs every pass again |
| 4002 | `SweepPassFailed` | Warning | One pass of the sweep failed, named in the entry; the next sweep runs it again |
| 4003 | `SweepStepFailed` | Warning | One step of the sweep failed, named in the entry; the next sweep runs it again |
| 4004 | `PumpFailed` | Warning | Reading a log for its subscribers failed with a coordination failure or a missing keyspace; the next wake-up or check reads it again |
| 4005 | `PumpFaulted` | Error | Reading a log for its subscribers failed unexpectedly; the next wake-up or check reads it again |
| 4006 | `ListenerConnectionFailed` | Warning | The listening connection failed; the listener connects again after the delay the entry names |
| 4007 | `ListenerDisposeFailed` | Debug | Disposing a broken listening connection failed; it holds nothing left to release |
| 4008 | `SweepPassFaulted` | Error | One pass of the sweep failed unexpectedly, named in the entry; the next sweep runs it again |
| 4009 | `SweepStepFaulted` | Error | One step of the sweep failed unexpectedly, named in the entry; the next sweep runs it again |

Each entry carries the failure as its exception. No entry carries a keyspace, key, channel, topic or connection string,
since they can name a tenant or hold a secret, so an entry shows that something failed, not for whom. See
[ADR-1035](https://github.com/NCodeGroup/coordination/blob/main/docs/decisions/1035-the-providers-log-every-failure-their-background-work-catches-with-event-ids-of-their-own.md).

## Verification

The provider passes every suite of the conformance kit against PostgreSQL 19 and PostgreSQL 15: concurrent runs of
every primitive checked for linearizability and declared delivery, and runs that end its own connections mid-run. The
suites' servers keep their data in memory, with `fsync` and `synchronous_commit` on as the probe requires. Its own
tests restart a PostgreSQL 19 server mid-run, with its data on disk: every failure is `unavailable`, a call that fails
as not applied never applies, and every subscription and watch resyncs and goes on delivering. When a server ends its
connections, the provider clears its data source's idle pool, so one lost connection doesn't cost a call for each
connection the pool held.

They also promote a standby of a PostgreSQL 19 primary and standby that share one container. When the standby is
promoted after asynchronous replication is cut, the writes the old primary acknowledged after the cut are lost, as
linearizable until a failover allows, and every subscription and watch resyncs on the new server when the failover
takes its numbers back. When a synchronous standby is promoted, no acknowledged write is lost, but the old primary
still answers reads until it stops, and it misses a write of the new primary, so every profile declares linearizable
until a failover.

Npgsql rechecks the hosts of a multi-host connection string every `Host Recheck Seconds`, 10 by default, and learns of
a promotion only then; until it does, a write fails as `unavailable`. Lower it in the connection string to shorten that
outage.
