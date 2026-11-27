# 1032. The PostgreSQL provider runs each operation as one statement or one short transaction of constant SQL

- **Status:** Proposed
- **Date:** 2026-11-27
- **Deciders:** NCode Group

## Context

The PostgreSQL provider talks to PostgreSQL 15 to 19 through Npgsql
([ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md)). Most of the
contracts' operations are several steps that must apply as one:

- A record write takes the keyspace's next revision and writes the record with it
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)). A
  compare-and-swap or compare-and-delete first checks the stored revision. A write bound to a session first checks that
  the session is live.
- A transaction checks up to 64 conditions and applies one of two branches of up to 64 writes.
- A lease, semaphore slot, election or claim grant checks the holder, takes the next fencing token and writes the grant
  ([ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)).
- A counter batch of 1 to 16 increments stops after the first that exceeds its limit, and a set member's expiry is
  measured on the server's clock
  ([ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)).
- Others are one step, such as recording an occurrence, incrementing a counter or reserving sequence numbers.

The forces:

- **F1. Atomic and isolated.** No other client sees a step's partial effect, and no interleaving breaks a check, which
  the history checker verifies
  ([ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md)).
- **F2. Conditional logic.** An outcome often depends on a value read in the same step, such as a stored revision or a
  counter over its limit.
- **F3. The server's clock.** Expiry is measured by the server wherever it can be
  ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)).
- **F4. Throughput of a keyspace.** Every write a watch can see takes its keyspace's next revision, so those writes
  queue on one another, and how long each holds its place bounds a keyspace's writes per second.
- **F5. Failures that say whether they took effect**
  ([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  ADR-1031 rule 4).
- **F6. Operations.** Nothing to deploy to the server beyond the provider's tables, rolling upgrades with two provider
  versions side by side, managed services, roles that aren't superusers, and poolers in transaction pooling.
- **F7. Code the product can keep correct.** Logic that is reviewed, unit-tested and debugged in one language.

What PostgreSQL and Npgsql offer (read 2026-11-27; sources under References):

- **Read committed**, PostgreSQL's default isolation level, gives each statement a snapshot taken as it starts. An
  `UPDATE` or `SELECT … FOR UPDATE` that finds a row another transaction is changing waits for that transaction to
  commit or roll back, then works on the row's newest version. Row locks are held to the end of the transaction.
- **Serializable** detects the anomalies that break a serial order and fails one transaction with `40001`, which the
  application retries.
- **A failing statement aborts its transaction**, and everything the transaction wrote is rolled back. A batch outside
  an explicit transaction runs as one implicit transaction.
- **Server-side functions** in PL/pgSQL run several statements and branches in one call, are installed with
  `CREATE FUNCTION` by a role allowed to create in the schema, and are replaced whole with `CREATE OR REPLACE`.
- **Advisory locks** lock an application-chosen number, for a session or for a transaction.
- **Timeouts.** `statement_timeout`, `lock_timeout` and `idle_in_transaction_session_timeout` bound a statement, a lock
  wait and an idle open transaction. `SET LOCAL` sets each for one transaction, which a pooler in transaction pooling
  supports, unlike a session's `SET`.
- **Clocks.** `now()` is the instant the transaction started, and `clock_timestamp()` the instant it is called, both in
  microseconds.
- **Observed** in a test run on 2026-11-27 against `postgres:18` (18.7) with `psql`:
  - A transaction that updated a keyspace row and held it waited out a second transaction's `UPDATE` of the same row;
    the second then saw, in its next statement, the row the first had inserted and committed.
  - A transaction that inserted a row, then failed with `22012`, left no row, and so did a two-statement batch outside
    an explicit transaction.
  - `SET LOCAL statement_timeout` failed a statement with `57014`; `SET LOCAL idle_in_transaction_session_timeout`
    ended a session idle in its transaction with `25P03`, and didn't outlive the transaction; `SET LOCAL lock_timeout`
    failed a lock wait with `55P03`.
  - `now()` and `clock_timestamp()` differed within one transaction.

## Decision

**An operation that one statement does runs as that statement. Every other operation runs as one short explicit
transaction at read committed, whose statements are constant SQL sent in batches, at most two round trips, and whose
decisions are made in C# between them. A change a watch can see first locks its keyspace's row. The provider installs
no functions, and no operation uses a serializable transaction, an advisory lock or two-phase commit.**

1. **One statement when one does it.** An operation whose whole effect is one statement runs as that statement outside
   an explicit transaction, such as an insert that skips an unexpired occurrence, an upsert that increments a counter
   and returns its count, or an upsert that reserves sequence numbers.
2. **A transaction otherwise.** Every other operation is one explicit transaction at read committed. Its first batch
   begins the transaction, sets its timeouts, takes its locks and reads what its outcome depends on. The provider
   decides in C# and sends a second batch that writes and commits, or rolls back. An operation whose writes don't
   depend on what it reads sends one batch.
3. **The keyspace's row first.** A change a watch can see, a record, a transaction, a session or what is bound to it,
   membership, or a lease, semaphore or election grant, first locks its keyspace's row, which holds the keyspace's
   revision, and takes the next revision only when it writes. Every such change in a keyspace waits for the one before
   it to commit, so their revisions follow their commit order, and a statement after the lock sees every earlier
   change. Other locks follow it in a fixed order, so two operations can't deadlock.
4. **Check, then write.** The first batch reads and checks everything the outcome depends on. An expected refusal, such
   as a conflict or a holder already present, rolls the transaction back and returns the outcome, never an error.
5. **Constant SQL.** Every statement is text shipped in the provider's assembly and written for this product, with
   every variable part a parameter. The schema's name, the one part that varies by host, is validated and quoted once
   when the provider starts, never per call.
6. **The server's clock.** An operation reads the current instant with `clock_timestamp()` once, after its locks are
   held, and uses it for every check and deadline in it. Every expiry the caller gives is a duration added to that
   instant, rounded up to the microsecond (ADR-1008).
7. **An expired row waits for writers.** A read that finds a row past its deadline, in state a watch can see, takes a
   share lock on its keyspace's row and reads again, with a fresh statement and a fresh instant, before it reports the
   row absent. A write in flight that found the row live before its deadline holds that row's lock, so it commits
   first, and no read can see the row absent before a write that relied on it being live. A read that finds only live
   rows, or none, takes no lock.
8. **Bounded time.** Every transaction sets `statement_timeout`, `lock_timeout` and
   `idle_in_transaction_session_timeout` with `SET LOCAL`, from the provider's options, so a paused or vanished caller
   holds its keyspace's row for a bounded time and a waiting operation fails as `unavailable` instead of hanging.
9. **Bounded work.** A statement's work is bounded by the contracts' limits: 64 conditions and writes, 16 increments,
   10,000 set members and 1,024 semaphore slots.
10. **Outcomes.** An error of severity `ERROR` rolls the transaction back, and so does a session that ends before the
    committing step is sent, so ADR-1031 rule 4 reports either with `OutcomeUnknown` false. Only a committing step,
    the `COMMIT` or the message that carries a single statement or a batch outside an explicit transaction, that was
    sent without a reply that settles it leaves the outcome unknown.
11. **No functions, and no serializable transactions, advisory locks or two-phase commit in an operation.** Rule 3's
    row locks serialize what an operation must serialize, and nothing else on the server is the provider's code.
    Applying the schema's migrations is not an operation: before the first one there is no row to lock, so
    [ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
    rule 8 serializes them with a transaction's advisory lock, which ends with its transaction and works through
    transaction pooling.

## Options considered

| Force | Constant SQL in short transactions | PL/pgSQL functions, one call per operation | Serializable transactions, retried | One statement per operation, through data-modifying CTEs |
| --- | --- | --- | --- | --- |
| F1 atomic and isolated | Row locks, keyspace first | Row locks inside the function | Yes, with retries | Only within one statement's snapshot |
| F2 conditional logic | In C#, between batches | In PL/pgSQL | In C# | In SQL predicates only |
| F3 server clock | `clock_timestamp()` after the locks | Same | Same | One statement's clock |
| F4 keyspace throughput | The row is held for about one round trip plus the work | Held only for the work | Every writer of a keyspace conflicts on its revision and retries | Held only for the work |
| F5 failure clarity | An error rolls back; unknown only after the committing statement | Same | Same, plus retries that may repeat | Same |
| F6 operations | Tables only; any pooler | Functions to install, version and replace in step with rolling upgrades | Tables only | Tables only |
| F7 code | C# and SQL statements | A second language on the server | C# and SQL | SQL that read committed's per-statement snapshots make subtle |

- **Constant SQL in short transactions.** Chosen: it meets F1, F2, F3, F5, F6 and F7 together, with every decision in
  C# that unit tests and the conformance kit reach. Its cost is F4: a write a watch can see holds its keyspace's row for
  about one round trip longer than a function would, roughly half a millisecond within one datacenter, which leaves a
  keyspace on the order of a thousand such writes per second. Coordination state changes far less often, and the
  hottest operations, occurrences and counters, are single statements that never take the keyspace's row.
- **PL/pgSQL functions.** Rejected: they would shorten that hold, but put a second body of logic on the server that two
  provider versions in a rolling upgrade must share or version side by side, add `CREATE FUNCTION` and `EXECUTE` to the
  provider's role, and move the decisions out of reach of C# tests. They gain throughput coordination state doesn't
  need.
- **Serializable transactions.** Rejected: every write a watch can see takes its keyspace's next revision, so
  concurrent writers of a keyspace conflict, fail with `40001` and retry, which costs more round trips than waiting on
  rule 3's lock, and a retry after an unknown outcome could apply twice.
- **One statement per operation.** Rejected as the general rule: a check and a write across several tables in one
  statement depends on how read committed rechecks a row it waited for, which is easy to get wrong and hard to review.
  Where one plain statement is the whole operation, rule 1 uses it.
- **Advisory locks.** Rejected for operations: a session's advisory lock outlives a pooled connection's use and doesn't
  work through transaction pooling, and a transaction's lock adds nothing to rule 3's row lock. Migrations have no such
  row before the first one creates the tables, so they take a transaction's lock (rule 11).

## Consequences

- One code path serves every profile, and a profile differs only in its declarations and its probe.
- A read is one statement unless it meets a row past its deadline, when rule 7 makes it wait for the keyspace's
  writers. The conformance kit's tests pause a write between its batches across a deadline to prove it.
- The SQL is product code: each statement is reviewed, kept small and tested by the conformance kit against a real
  server, under concurrency and with faults injected.
- A keyspace's writes that a watch can see are serialized by its row, so a keyspace per tenant spreads them, as it does
  on the RESP provider.
- The layout decision has to give every keyspace one row with its revision, and the notification decision can number
  each change in the transaction that makes it.
- You chose wrong if the conformance kit's runs or a benchmark show a keyspace's writes limited by the round trip its
  transactions hold the row for, at a load a consumer needs. Then a record that supersedes this one weighs functions
  for those operations.

## References

- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md),
  [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md),
  the RESP provider's counterpart,
  [ADR-1031](1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md),
  [ADR-1033](1033-postgresql-state-lives-in-tables-the-provider-migrates-shared-by-every-keyspace-with-each-keyspaces-revision-on-its-row.md)
- PostgreSQL 19 documentation, read 2026-11-27:
  [Transaction isolation](https://www.postgresql.org/docs/19/transaction-iso.html),
  [Explicit locking](https://www.postgresql.org/docs/19/explicit-locking.html),
  [Client connection defaults](https://www.postgresql.org/docs/19/runtime-config-client.html),
  [SET](https://www.postgresql.org/docs/19/sql-set.html),
  [Date/time functions](https://www.postgresql.org/docs/19/functions-datetime.html),
  [CREATE FUNCTION](https://www.postgresql.org/docs/19/sql-createfunction.html) and
  [PL/pgSQL](https://www.postgresql.org/docs/19/plpgsql.html)
- Npgsql documentation, read 2026-11-27: [Basic usage](https://www.npgsql.org/doc/basic-usage.html), batching and
  transactions
- [PgBouncer: Features](https://www.pgbouncer.org/features.html), read 2026-11-27: `SET LOCAL` and advisory locks under
  each pooling mode
- A test run on 2026-11-27 with `psql` against a `postgres:18` (18.7) container: two transactions on one keyspace row,
  a failing statement after an insert, and `statement_timeout`, `lock_timeout` and
  `idle_in_transaction_session_timeout` set with `SET LOCAL`
