# 1033. PostgreSQL state lives in tables the provider migrates, shared by every keyspace, with each keyspace's revision on its row

- **Status:** Proposed
- **Date:** 2026-11-27
- **Deciders:** NCode Group

## Context

The PostgreSQL provider runs each operation as one statement or one short transaction that first locks its keyspace's
row when a watch can see the change
([ADR-1032](1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md)).
It has to decide where its state lives, how keys are stored, how expiry works, and who creates and upgrades its
tables:

- **F1. One revision per keyspace.** A record's revision is greater for every later write in its keyspace, and watches
  and transactions are written in it
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md) rule 1).
- **F2. Listing without a scan, with any key.** A list returns the records under a prefix, a page at a time, each
  continued by a token of at most 1,024 characters (ADR-1014 rule 3). A key holds up to 16 segments of up to 256
  characters, several kilobytes encoded, and the RESP provider indexes its records by digests of their segments for the
  same reason ([ADR-1027](1027-resp-records-live-in-one-table-per-keyspace-indexed-by-digests-of-their-segments.md)).
- **F3. Many keyspaces, made at runtime.** A keyspace is a cluster name and up to 8 typed segments, and NCode.Identity
  uses one per tenant, so a database holds thousands, each appearing when it is first used
  ([Design: Keyspace](../designs/coordination.md#keyspace)).
- **F4. Expiry on the server's clock.** State past its deadline is absent to every read at once
  ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)),
  a watch learns of the deletion, and storage is reclaimed in bounded batches
  ([ADR-0142](project-template/0142-data-that-grows-with-use-is-pruned-in-bounded-batches.md)). PostgreSQL has no
  time to live of its own.
- **F5. Durability.** An occurrence lost after a crash accepts a replay, so the provider keeps what it declares
  ([ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md) rule 8).
- **F6. Who owns the schema.** Some hosts let a library create its tables when it starts. Others apply every change to
  their database through their own process, as NCode.Identity does with a migration worker and a drift check. A
  rolling upgrade runs two provider versions on one schema at once. A managed service's role isn't a superuser, but can
  create schemas and tables in its own database.

What PostgreSQL offers (read 2026-11-27; sources under References):

- **`bytea`** compares byte by byte, as unsigned bytes, with a shorter value first when it is a prefix of the longer.
  A B-tree index on it serves a range such as `k >= p AND k < q` without a collation. A B-tree entry holds at most
  about a third of a page, 2,704 bytes on the default 8 kB page.
- **Unlogged tables** skip the write-ahead log: they are faster, emptied after a crash and not replicated to standbys.
- **DDL is transactional**: `CREATE SCHEMA` and `CREATE TABLE` in a transaction that rolls back leave nothing.
- **Identifiers** are at most 63 bytes.
- **Catalogs** grow with every table and index, and a database with a table per tenant pays that per tenant.
- **Observed** in a test run on 2026-11-27 against `postgres:18` (18.7): `'\x01' < '\x0100'`, `'\xff' > '\x01ff'` and
  `'\x80' > '\x7f'` as `bytea`; a range on a `bytea` primary key planned as an index-only scan; and a schema and table
  created in a transaction that rolled back left no schema.

## Decision

**The provider keeps its state in a fixed set of ordinary tables in one schema it names and migrates, shared by every
keyspace. A keyspace is a row that holds its revision, created on first use. Keys are indexed by the digests of their
segments, so a prefix is a byte prefix. Every deadline is a timestamp on the server's clock that reads honor at once and
a sweep removes in bounded batches.**

1. **One schema, fixed tables.** Every table lives in one schema, `coordination` unless the provider's options name
   another identifier of at most 63 bytes. The set of tables is fixed by the provider's version, and nothing creates a
   table, index or schema while operations run. Each primitive's tables are defined by the plan that builds it.
2. **A keyspace is a row.** The keyspace table holds each keyspace's encoded name, a numeric id, its revision and what
   the notification decision keeps beside it. The id is a random 64-bit number drawn when the row is created, never a
   sequence's, so an id a failover lost is never given to another keyspace. An operation inserts the row when it is
   absent. Every other table's rows
   start their primary key with their keyspace's id and reference its row, so a row can't outlive or precede its
   keyspace. The provider caches an id once the transaction that read or created it commits, and drops it when a
   statement finds the id missing, as after a failover that lost the row, or when the server's identity changes
   ([ADR-1034](1034-postgresql-notifications-and-watches-are-numbered-in-the-writing-transaction-kept-in-a-bounded-log-and-woken-by-notify.md)
   rule 7), then resolves it again.
3. **Keys indexed by digests of their segments.** A key's index value is the 32-byte SHA-256 digest of each encoded
   segment, in order, as `bytea`: at most 512 bytes, well under a B-tree entry's limit, and a prefix's index value is a
   byte prefix of the index value of every key under it. The typed key itself is stored beside it. A row's primary key
   is its keyspace's id, the index value and the key's partition depth, since keys with the same segments and different
   depths are different keys; two keys share a primary key only if SHA-256 collides. A listing is one range on the
   index value, whatever the depth, never a scan, in the order of the digests, which ADR-1014 rule 3 lets the provider
   choose. A page's continuation token is the last index value and depth it returned in Base64Url, under 700 characters.
4. **Atomic across partitions.** Every change a watch can see in a keyspace is serialized by the keyspace's row
   (ADR-1032 rule 3), so any operation within a keyspace is atomic, and the provider declares
   `atomicity.across-partitions`. A key's partition depth doesn't change where it is stored.
5. **Ordinary tables.** Every table is logged, so its rows survive a crash and reach every standby. The provider never
   creates an unlogged table.
6. **Deadlines on the server's clock.** A row with a lifetime stores its deadline as a `timestamptz`, the instant of
   ADR-1032 rule 6 plus the duration.
   - Every read treats a row past its deadline as absent, once ADR-1032 rule 7 has let the keyspace's writers finish.
   - A write that meets such a row removes or replaces it, and when a watch can see it, numbers and announces its
     deletion first, in the same transaction. A change that removes, replaces or rebinds a row bound to an ended session
     names the session in its change log entry (ADR-1034 rule 5).
   - A read of state bound to a session treats it as absent once the session isn't live.
   - A session's end, by close or by expiry, is one change: it removes the session's row, takes one revision and logs
     one entry naming the session (ADR-1034 rule 1). What is bound to the session is absent to every read from that
     revision, and is left for the sweep to reclaim, so ending a session does bounded work however much it held.
7. **A sweep in bounded batches.** Every provider instance runs a sweep at an interval, 5 seconds by default and set in
   its options, that removes rows past their deadline, at most 1,000 per statement by default. It skips rows another
   instance holds, so instances share the work. A deletion a watch can see is swept in a transaction that locks its
   keyspace's row and numbers it, as rule 6 does. Rows bound to a session that has ended are already absent, so the
   sweep reclaims them in bounded batches without taking a revision, once the log entry that ended their session has
   left the change log (ADR-1034 rule 6), so a watch that reads that entry still finds the keys bound to the session.
8. **Migrations.** The provider ships its schema as numbered, forward-only migrations of constant SQL. A table in the
   schema records the version applied and the oldest provider version that can still use it.
   - By default the provider applies what is missing when it starts, each migration in one transaction, serialized
     across instances by a transaction's advisory lock. Operations never take one (ADR-1032 rule 11).
   - A host that applies schema changes itself turns that off and takes the provider's scripts, which the package
     exposes, into its own process.
   - Either way the probe fails the start when the schema is older than the provider needs, or newer and no longer
     usable by it.
9. **The role.** Applying migrations needs a role that can create the schema or owns it. Without migrations, the role
   needs only to use the schema and to read and write its tables. The probe checks the privileges the configuration
   needs, and fails the start as `access-denied` without them.

## Options considered

| Force | Shared tables keyed by keyspace | A schema or tables per keyspace | One generic key-value table | Text keys under a collation |
| --- | --- | --- | --- | --- |
| F1 revision | A keyspace row, locked by each change | A row in each keyspace's schema | A keyspace row | A keyspace row |
| F2 listing | A range of segment digests on a primary key | Same, per table | A range, mixed with every primitive's rows | A range whose order depends on the collation |
| F3 many keyspaces | Rows | DDL at runtime, catalogs that grow per tenant | Rows | Rows |
| F4 expiry | A deadline column and index per table | Same, per table | One deadline index for every kind of row | Same as shared tables |
| F5 durability | Logged | Logged | Logged | Logged |
| F6 schema owner | Fixed tables, migrated at start or by the host | DDL privileges at runtime, for every host | Fixed table | Fixed tables |

- **Shared tables keyed by keyspace.** Chosen: it keeps ADR-1014 and ADR-1032 as they are, needs no DDL while
  operations run, and lets each primitive's checks be predicates on typed columns. Its limit is that every keyspace's
  rows share each table's indexes, which coordination state, small and few per key, keeps small.
- **A schema or tables per keyspace.** Rejected: thousands of tenants would mean thousands of schemas, DDL rights for
  the provider's role while it runs, and catalogs that grow with every tenant.
- **One generic key-value table.** Rejected: every primitive's state would be opaque values whose checks the provider
  decodes, and expiry, membership and grants would share one shape that fits none of them.
- **Text keys under a collation.** Rejected: a listing's range and its pages would depend on the database's collation,
  which a host chooses, while `bytea` compares the same on every server.
- **The encoded key itself as the index.** Rejected: the longest keys are several kilobytes, past a B-tree entry's
  limit, and a continuation token holding the last key would pass 1,024 characters.
- **Native partitioning by keyspace.** Rejected: a partition is a table, so it brings back the cost of a table per
  keyspace.
- **Expiry by a scheduler in the server**, such as `pg_cron`. Rejected: it is an extension a managed service may not
  offer, and it can't number deletions as the provider does.
- **EF Core migrations.** Rejected: they would bring EF Core into every host of the provider, and a host on EF Core
  takes the provider's scripts into its own migrations through rule 8.

## Consequences

- A keyspace costs a row, so a keyspace per tenant is cheap, and its first operation adds the row.
- Each primitive's plan defines its tables, their deadline columns and indexes, and the sweep's statement for them,
  within these rules.
- Expired state is absent to reads at once, and its storage is reclaimed within a sweep interval while any provider
  instance runs.
- A host that applies schema changes itself runs the provider's scripts before the provider's new version starts, and
  the probe tells it when it didn't.
- You chose wrong if shared tables' indexes or the sweep become a measurable cost at a consumer's scale, or if a
  managed service the product targets refuses the schema's DDL. Then a record that supersedes this one weighs
  partitioned tables or a schema per cluster.

## References

- [Design: Keyspace](../designs/coordination.md#keyspace)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md), the RESP
  provider's counterpart,
  [ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md),
  [ADR-1032](1032-the-postgresql-provider-runs-each-operation-as-one-statement-or-one-short-transaction-of-constant-sql.md),
  and the template's [ADR-0142](project-template/0142-data-that-grows-with-use-is-pruned-in-bounded-batches.md)
- `src/NCode.Coordination.Abstractions/Keys/Keyspace.cs`, `CoordinationKey.cs` and `KeySegment.cs`: the keyspace's
  and the key's limits and the partition depth, and `Operations/CoordinationLimits.cs`: the continuation token's
- PostgreSQL 19 documentation, read 2026-11-27: [Binary data types](https://www.postgresql.org/docs/19/datatype-binary.html),
  [B-tree implementation](https://www.postgresql.org/docs/19/btree.html), the size of an index entry,
  [CREATE TABLE](https://www.postgresql.org/docs/19/sql-createtable.html), its `UNLOGGED` clause,
  [Identifiers and key words](https://www.postgresql.org/docs/19/sql-syntax-lexical.html),
  [Table partitioning](https://www.postgresql.org/docs/19/ddl-partitioning.html),
  [Advisory locks](https://www.postgresql.org/docs/19/explicit-locking.html#ADVISORY-LOCKS) and
  [Privileges](https://www.postgresql.org/docs/19/ddl-priv.html)
- NCode.Identity's [ADR-1073](https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1073-one-postgresql-database-holds-the-hosts-schema-and-key-ring-migrated-by-a-worker-and-drift-checked.md),
  a product of this organization: a host that migrates its database with a worker
- A test run on 2026-11-27 with `psql` against a `postgres:18` (18.7) container: `bytea` comparisons, a range on a
  `bytea` primary key, and DDL in a transaction that rolled back
