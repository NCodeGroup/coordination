# 1031. The PostgreSQL provider speaks through Npgsql, and can share the host's data source

- **Status:** Proposed
- **Date:** 2026-11-27
- **Deciders:** NCode Group

## Context

`NCode.Coordination.PostgreSql` is the third provider of the first stable release
([ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md)):
a provider that needs no infrastructure beyond the database a service already runs
([Design: Rollout and Phases](../designs/coordination.md#rollout-and-phases)). NCode.Identity, the first consumer, runs
PostgreSQL as Akamai's managed three-node service in production and as CloudNativePG in its lab, and names Amazon RDS
Multi-AZ for a deployment on AWS ([hosting analysis](https://github.com/NCodeGroup/identity/blob/main/docs/hosting-analysis.md)).
Every later PostgreSQL decision is built on the client that carries its statements. The client has to meet these
forces:

- **F1. Every supported server.** PostgreSQL 15 to 19, the majors the PostgreSQL project supports, on a server of the
  host's own, CloudNativePG or a managed service, with TLS and a role that isn't a superuser, which Akamai's service
  doesn't grant.
- **F2. Every host.** Each runtime identifier a .NET host image targets, Alpine's musl and arm64 included, with no
  native library to ship, and NativeAOT.
- **F3. Originality.** A permissive license on the package and every transitive dependency, which
  `build/Test-Licenses.ps1` checks ([ADR-1009](1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md)),
  used through its public API and documentation only ([ORIGINALITY.md](../../ORIGINALITY.md)).
- **F4. Failures that say whether they took effect.** A `CoordinationException`'s `OutcomeUnknown` is false only when
  the provider knows the operation didn't apply
  ([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
  rule 2), and the history checker relies on it. PostgreSQL rolls a transaction back when one of its statements fails
  or its session ends before it commits, which answers that question more often than a RESP server can.
- **F5. What the primitives need.** Parameterized statements, explicit transactions, several statements in one round
  trip, `LISTEN` and `NOTIFY` on a connection held open, and the error's SQLSTATE.
- **F6. The host's own data source.** A host that already talks to the database, such as NCode.Identity through EF
  Core or an Aspire app through `AddNpgsqlDataSource`, can share its connection pool instead of opening another.
- **F7. Cost.** What the client costs to build, keep current and keep correct across PostgreSQL versions and managed
  services.
- **F8. One client across the organization's code.** NCode.Identity reaches PostgreSQL through EF Core 10.0.15 and
  `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, which depends on Npgsql 10. A coordination provider in the same
  process shares that assembly, and the data source of F6 is one of its types.

What the PostgreSQL servers offer (read 2026-11-27; sources under References):

- **Versions.** PostgreSQL 19.0 was released 2026-11-19. The supported majors are 15 to 19, each for five years; 14
  reached its end of life on 2026-11-12. Akamai's managed service offers 15 to 18.
- **Primaries only.** A hot standby refuses writes, `nextval`, `LISTEN` and `NOTIFY`. Npgsql's multi-host connection
  strings pick the primary with `Target Session Attributes=primary`, which it detects with `pg_is_in_recovery()`.
- **Connection poolers.** PgBouncer supports `LISTEN` and session-level settings only in session pooling, never in
  transaction pooling, which is the default of the pooler Aiven runs for Akamai's service. Akamai's service allows
  about 100 connections per GiB of memory, about 100 on its 1 GB plan.

The candidates (read 2026-11-27 from nuget.org and each project's documentation; sources under References):

- **Npgsql** 10.0.4 and 11.0.0, both published 2026-11-20, under the PostgreSQL License, with about 1.15 billion
  downloads. 11.0.0 targets `net10.0` only, depends only on `Microsoft.Extensions.DependencyInjection.Abstractions` and
  `Microsoft.Extensions.Logging.Abstractions` 11.0.0, absorbs `Npgsql.DependencyInjection`, and removes multiplexing.
  10.0.4 targets `net8.0`, `net9.0` and `net10.0`. `Npgsql.EntityFrameworkCore.PostgreSQL` 11.0.0 targets `net11.0` and
  depends on Npgsql 11, and its 10.0.4 targets `net10.0` and depends on Npgsql 10.0.4. Npgsql is fully managed and,
  since 8.0, compatible with NativeAOT and trimming. Its `NpgsqlDataSource` holds one pool and hands out connections;
  `NpgsqlBatch` sends several statements in one round trip, wrapped in an implicit transaction unless an explicit one
  is open; a connection raises `Notification` for each notification it receives, and `WaitAsync` waits for one;
  `Keepalive` pings an idle connection, such as one waiting for notifications. A `PostgresException` carries the
  server's SQLSTATE, and an `NpgsqlException` that isn't one reports a network failure or a timeout. A timeout or a
  cancellation sends PostgreSQL a cancel request. Npgsql never retries a failed command by itself. A connection opened
  inside an ambient `TransactionScope` enlists in it unless its connection string says `Enlist=false`. EF Core's
  PostgreSQL provider and Aspire's PostgreSQL integrations are built on it.
- **ODBC**, through `System.Data.Odbc` and the psqlODBC driver, a native library under the LGPL installed on every host.
- **A client of the product's own**, written from the PostgreSQL frontend/backend protocol, version 3, over
  `System.IO.Pipelines` and `SslStream`.

## Decision

**`NCode.Coordination.PostgreSql` talks to PostgreSQL through Npgsql, used only through its public API, on the major
line the host's EF Core provider runs. A host either gives the provider a connection string, and the provider owns its
data source, or hands it the host's `NpgsqlDataSource`. The client's types stay out of every contract except the
provider's configurator.**

1. **The package.** `NCode.Coordination.PostgreSql` references Npgsql 10.0.4, the newest release of the line that EF
   Core 10's PostgreSQL provider builds on. Npgsql 11 requires hosts to move to it with EF Core 11, which needs .NET 11,
   so moving the provider to Npgsql 11 is a planned migration once the product's hosts can. Until then a Dependabot
   `ignore` rule holds back Npgsql's next major version, with a comment naming the issue that tracks lifting it.
2. **The data source.** The provider's configurator takes either a connection string, from which the provider builds,
   owns and disposes one `NpgsqlDataSource`, or a factory that returns the host's `NpgsqlDataSource`, which the provider
   uses and never disposes. `NCode.Coordination.Abstractions` and `NCode.Coordination` reference no client type.
3. **One internal seam.** The primitives reach the server through one internal layer that runs statements and
   transactions, holds the listening connection and classifies failures. No primitive calls the client directly.
   Changing the client changes this layer, not the primitives.
4. **Failures.** The seam turns the client's exceptions into `CoordinationException`s. An operation's committing step
   is its `COMMIT`, or, for a single statement or a batch outside an explicit transaction, the whole message that ends
   in its sync.
   - `OutcomeUnknown` is false when the provider knows the transaction ended without committing: PostgreSQL answered a
     statement, the committing step included, with an error of severity `ERROR`, which rolls the transaction back, or
     the connection failed before the committing step was sent, since the server rolls back the open transaction of a
     session that ends, as when `idle_in_transaction_session_timeout` ends it between two batches.
   - Once the committing step was sent, a timeout, a dropped connection, a `FATAL` or `PANIC` error, or `08007`
     (transaction resolution unknown) leaves the outcome unknown.
   - A cancellation of the caller's token throws `OperationCanceledException`, as ADR-1011 rule 4 says of every
     operation. It leaves the outcome unknown once the committing step was sent, and known otherwise.
   - An error maps to a code by its SQLSTATE, never by its message text: classes 08, 40 and 57, `53300`, `55P03`,
     `25P03` and `25P04` to `unavailable`, class 28 and `42501` to `access-denied`, and `22003` to `limit-exceeded`.
   - A test against a real server with injected faults proves each mapping.
5. **The listening connection.** Notifications and watches listen on one physical connection the provider opens from
   the data source and holds for its lifetime, with `Keepalive` set. Every other operation takes a pooled connection for
   that operation only. The listening connection must reach PostgreSQL directly or through session pooling, which the
   provider's documentation says, and the probe refuses a data source that multiplexes.
6. **The primary.** Every connection must reach the primary. A multi-host data source is filtered to the primary, and
   the probe fails the start on a server in recovery.
7. **No ambient transaction.** Npgsql enlists a connection in the ambient `System.Transactions` transaction by
   default. The provider opens and uses every connection, the listening one included, with the ambient transaction
   suppressed, and builds its own data source with `Enlist=false`, so a host's `TransactionScope` never takes in a
   coordination operation or rolls one back after it reported success.
8. **Profiles and the probe.** The startup probe of
   [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md) runs through the
   seam and fails the start on a server older than 15, on a standby, on `fsync` off, on `synchronous_commit` off as the
   provider's sessions see it, since a crash then loses commits already acknowledged, or on a role that lacks a
   permission an operation needs. A shared data source is probed the same way. Every profile declares
   `consistency.linearizable-until-failover` until the conformance kit's failover runs show more. The provider ships
   three profiles, which differ in durability:
   - **`PostgreSqlStandalone`** declares `durability.durable` for a server with no standby a failover could promote,
     whose acknowledged commits survive its crash on its own disk. The probe can't see standbys a role isn't allowed to
     list, so the profile states the requirement, as the RESP provider's Garnet profile states its transaction mode.
   - **`PostgreSqlAsynchronous`** declares `durability.replicated-asynchronously` for a server whose standbys replicate
     asynchronously, the default of Aiven, which runs Akamai's service, and of CloudNativePG: a standby promoted after a
     failure may lack writes the old primary acknowledged.
   - **`PostgreSqlSynchronous`** declares `durability.durable` for a server whose commits wait for a synchronous
     standby. The probe requires `synchronous_standby_names` to name one and `synchronous_commit` to be `on` or
     `remote_apply` as the provider's sessions see it, for single statements as for transactions, which the host sets
     for the provider's role or connection.
   - A managed service whose replication the probe can't see, such as Amazon RDS Multi-AZ's synchronous standby, is a
     further profile a host registers, as ADR-1003 rule 4 allows, and the provider may ship later. A deployment whose
     durability no profile states declares `durability.volatile` in a profile of its own.

## Options considered

| Force | Npgsql 10.0.4 | Npgsql 11.0.0 | ODBC with psqlODBC | A client of the product's own |
| --- | --- | --- | --- | --- |
| F1 servers | PostgreSQL 15–19, TLS, multi-host | Same | Same, through the driver | Whatever the conformance kit proves |
| F2 hosts | Managed, NativeAOT | Managed, NativeAOT | A native driver installed on every host | Managed |
| F3 license | PostgreSQL License | PostgreSQL License | LGPL driver | None to check |
| F4 failure classification | SQLSTATE and exception type, verified by tests | Same | SQLSTATE through ODBC's diagnostics | Exact, from its own socket writes |
| F5 statements | Batches, transactions, notifications | Same | No batches or notifications in `System.Data.Odbc` | All, once written |
| F6 host's data source | `NpgsqlDataSource` from EF Core 10, Aspire and `AddNpgsqlDataSource` | Only in a host already on Npgsql 11 | A second connection pool | A second connection pool |
| F7 cost | A dependency to keep current, and one planned major migration | A dependency to keep current | A native driver per platform | The protocol, TLS, authentication, failover and pooling |
| F8 one client | The Npgsql that NCode.Identity's EF Core provider runs | A second major version beside NCode.Identity's | A second client | A second client |

- **Npgsql 10.0.4.** Chosen: it meets every force, and a host on EF Core 10, NCode.Identity among them, shares its data
  source with the provider without changing its own stack. Its cost is a planned move to the next major version, which
  rule 1 tracks.
- **Npgsql 11.0.0.** Rejected for now, although it is the newest release and drops multiplexing, which rule 5 refuses
  anyway. A host on EF Core 10 would run its EF Core provider on Npgsql 11, a pairing neither project ships, and
  couldn't hand the provider its data source without moving to .NET 11 (F6, F8).
- **ODBC.** Rejected: a native LGPL driver on every host fails F2 and F3, and `System.Data.Odbc` lacks batches and
  notifications.
- **A client of the product's own.** Rejected: it would answer F4 exactly, but at the cost of reimplementing the
  protocol, authentication methods, TLS, failover and pooling of a mature client (F7), and it couldn't share a host's
  pool (F6).
- **No PostgreSQL provider.** Rejected: ADR-1007 makes it part of the first stable release.

## Consequences

- A host on PostgreSQL, including one already using Npgsql through EF Core or Aspire, adds the provider without a new
  connection pool, apart from the one listening connection.
- The provider's operations, its notifications and its watches are decided over SQL statements, transactions and
  `LISTEN`, with no installed server extension.
- A host whose pooler runs transaction pooling gives the provider a data source that reaches PostgreSQL directly or
  through session pooling for its listening connection, or doesn't use notifications and watches.
- A major version of Npgsql changes the seam and the configurator, and nothing else. Rule 3 keeps that true, and
  review holds the primitives to it.
- A wrong failure classification would mislead the history checker. The fault-injection tests of rule 4 are part of
  the provider's conformance run, not an extra.
- You chose wrong if a profile's guarantee fails in the conformance kit because of the client rather than the server,
  or if a host the product serves can't share its data source because of the Npgsql version. Then a record that
  supersedes this one moves the provider to Npgsql 11, or takes both lines.

## References

- [Design: Packages](../designs/coordination.md#packages) and
  [Rollout and Phases](../designs/coordination.md#rollout-and-phases)
- [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md),
  [ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md),
  [ADR-1009](1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md),
  [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md), the
  RESP provider's counterpart
- [nuget.org](https://www.nuget.org/) registration and search APIs, read 2026-11-27: versions, publication dates,
  download counts, licenses, target frameworks and dependencies of Npgsql, Npgsql.DependencyInjection,
  Npgsql.EntityFrameworkCore.PostgreSQL and Testcontainers.PostgreSql
- Npgsql documentation, read 2026-11-27: [Basic usage](https://www.npgsql.org/doc/basic-usage.html),
  [Performance](https://www.npgsql.org/doc/performance.html),
  [Waiting for notifications](https://www.npgsql.org/doc/wait.html),
  [Keepalive](https://www.npgsql.org/doc/keepalive.html),
  [Connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html),
  [Failover and load balancing](https://www.npgsql.org/doc/failover-and-load-balancing.html),
  [Prepared statements](https://www.npgsql.org/doc/prepare.html),
  [Compatibility](https://www.npgsql.org/doc/compatibility.html),
  [Logging and dependency injection](https://www.npgsql.org/doc/diagnostics/logging.html), the API pages of
  [`NpgsqlException`](https://www.npgsql.org/doc/api/Npgsql.NpgsqlException.html) and
  [`PostgresException`](https://www.npgsql.org/doc/api/Npgsql.PostgresException.html), and the release notes of
  [5.0](https://www.npgsql.org/doc/release-notes/5.0.html) and [11.0](https://www.npgsql.org/doc/release-notes/11.0.html)
- [PostgreSQL: Versioning policy](https://www.postgresql.org/support/versioning/), read 2026-11-27
- PostgreSQL 19 documentation, read 2026-11-27: [NOTIFY](https://www.postgresql.org/docs/19/sql-notify.html),
  [Hot Standby](https://www.postgresql.org/docs/19/hot-standby.html), the commands a standby refuses,
  [Message flow](https://www.postgresql.org/docs/19/protocol-flow.html), how a session's end and an error end a
  transaction, [SQLSTATE codes](https://www.postgresql.org/docs/19/errcodes-appendix.html) and
  [Write-ahead log settings](https://www.postgresql.org/docs/19/runtime-config-wal.html), `fsync` and
  `synchronous_commit`
- A test run on 2026-11-27 with `psql` against a `postgres:18` (18.7) container, as a role that isn't a superuser:
  `SHOW fsync`, `SHOW synchronous_commit`, `SHOW synchronous_standby_names` and `pg_is_in_recovery()`
- [PgBouncer: Features](https://www.pgbouncer.org/features.html) and
  [Configuration](https://www.pgbouncer.org/config.html), read 2026-11-27: what each pooling mode supports
- Akamai documentation, read 2026-11-27:
  [Managed Databases powered by Aiven](https://techdocs.akamai.com/cloud-computing/docs/aiven-database-clusters) and
  [Database engines and plans](https://techdocs.akamai.com/cloud-computing/docs/database-engines-plans)
- Aiven documentation, read 2026-11-27:
  [High availability](https://aiven.io/docs/products/postgresql/concepts/high-availability),
  [Advanced parameters](https://aiven.io/docs/products/postgresql/reference/advanced-params),
  [Upgrade and failover](https://aiven.io/docs/products/postgresql/concepts/upgrade-failover) and
  [Connection pooling](https://aiven.io/docs/products/postgresql/concepts/pg-connection-pooling)
- CloudNativePG 1.30 documentation, read 2026-11-27: [Replication](https://cloudnative-pg.io/docs/1.30/replication/),
  [Failover](https://cloudnative-pg.io/docs/1.30/failover/) and
  [Service management](https://cloudnative-pg.io/docs/1.30/service_management/)
- Amazon RDS documentation, read 2026-11-27:
  [Multi-AZ DB instance deployments](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/Concepts.MultiAZSingleStandby.html)
  and [Multi-AZ DB cluster deployments](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/multi-az-db-clusters-concepts.html)
- [NCode.Identity hosting analysis](https://github.com/NCodeGroup/identity/blob/main/docs/hosting-analysis.md) and its
  `Directory.Packages.props`, products of this organization: the first consumer's PostgreSQL services and its Npgsql
  line
