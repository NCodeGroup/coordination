# 1023. The RESP provider runs each operation as one command or one Lua script, and never as a client-side transaction

- **Status:** Proposed (amended by 1025, 1027)
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

The RESP provider talks to Valkey first, then Garnet, through StackExchange.Redis
([ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md)). Most of
the contracts' operations are several steps that must apply as one:

- A record write takes the keyspace's next revision and writes the record with it
  ([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)). A
  compare-and-swap or compare-and-delete first checks the stored revision. A write bound to a session first checks that
  the session is live.
- A transaction checks up to 64 conditions and applies one of two branches of up to 64 writes.
- A lease, semaphore slot, election or claim grant checks the holder, takes the next fencing token and writes the grant
  ([ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)).
- A counter increment sets the time to live only when it creates the counter. A batch of 1 to 16 increments stops after
  the first that exceeds its limit. A set member's expiry is measured on the server's clock
  ([ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)).
- Others are one step, such as recording an occurrence (`SET` with `NX` and `PX`), taking a record (`GETDEL`) or
  reserving sequence numbers (`INCRBY`).

The forces:

- **F1. Atomic and isolated on every profile.** No other client sees a step's partial effect, and no interleaving breaks
  a check, which the history checker verifies
  ([ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md)).
- **F2. Conditional logic on the server.** An outcome often depends on a value read in the same step, such as a stored
  revision or a counter over its limit.
- **F3. The server's clock.** Expiry is measured by the server wherever it can be
  ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)).
- **F4. Cluster mode.** Every key a step touches is known before it runs, so it can be routed and locked.
- **F5. Failures that say whether they took effect**
  ([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)).
- **F6. Operations.** Nothing to deploy to the server beyond its settings, rolling upgrades with two provider versions
  side by side, managed services and restrictive ACL users.

What the servers and the client offer (read 2026-11-25; sources under References):

- **Valkey** runs a Lua 5.1 script atomically, blocking every other command while it runs. A script must receive every
  key name it touches as an argument (`KEYS`). The script cache is volatile, and is lost on restart, failover or
  `SCRIPT FLUSH`. Valkey 7.0 adds functions (`FUNCTION LOAD`, `FCALL`): named libraries that persist and replicate, and
  that are replaced as a whole. Valkey 8.1 adds `SET … IFEQ`, and 9.0 adds `DELIFEQ`. `MULTI`/`EXEC` with `WATCH` runs
  queued commands atomically but can't branch on what they read.
- **Garnet** supports `EVAL`, `EVALSHA`, `SCRIPT LOAD`, `SCRIPT EXISTS`, `SCRIPT FLUSH`, `MULTI`, `EXEC` and `WATCH`. It
  doesn't support `FUNCTION` or `FCALL`. It runs Lua only when started with `--lua`. Only with `--lua-transaction-mode`
  does it lock a script's keys for the script's whole run. `--lua-script-timeout` and
  `--lua-script-memory-limit` bound a script.
- **StackExchange.Redis** runs a script with `ScriptEvaluate(Async)`, which takes the keys and arguments as arrays and
  returns a `RedisResult`, or with `ScriptEvaluateResp(Async)`, which takes them as `ReadOnlyMemory` and returns a
  pooled `RespResult` that the caller disposes and that has no public constructor. Both go through the client's script
  cache: unless a call passes `CommandFlags.NoScriptCache`, the first call sends `EVAL` and later calls send `EVALSHA`,
  and on `NOSCRIPT` it resends the script by itself. It routes a script by its `KEYS`, and reads either reply by
  category (scalar, aggregate, null, error) the same way in RESP2 and RESP3. It runs `MULTI`/`EXEC` through
  `ITransaction` with a fixed set of preconditions and doesn't expose `WATCH` on the multiplexer.
- **Observed** in a test run on 2026-11-25 against `valkey/valkey:9` and Garnet 2.2.14 started with `--lua
  --lua-transaction-mode true`:
  - On both, a script read the server's clock with `TIME` and set a key with `PX`.
  - On both, a write a script made before it returned an error stayed applied.
  - Valkey answered `CONFIG GET lua*` with `lua-time-limit`, and Garnet answered with nothing.
  - Garnet's error reply for a script error carried the prefix twice (`ERR ERR stop`).
- **Observed** in a second test run on 2026-11-25, through StackExchange.Redis 3.4.35's `ScriptEvaluateAsync` with a
  script's text, against `valkey/valkey:9.2.0` and Garnet 2.2.13 started with `--lua --lua-transaction-mode true`:
  - On Valkey, `MONITOR` showed `SCRIPT LOAD` and `EVAL` for the first call and `EVALSHA` for the next two.
  - After `SCRIPT FLUSH`, the next call returned its result on both, with no error reaching the caller; on Valkey it
    sent `EVALSHA`, then `SCRIPT LOAD` and `EVAL`.
  - On Valkey, a user denied `SCRIPT` ran the script on every call, each time with `EVAL`.

## Decision

**An operation that is one server command runs as that command. Every other operation runs as one Lua script that
receives all its keys, checks before it writes, and runs through StackExchange.Redis's script cache. The provider uses
no `MULTI`/`EXEC`, no functions and no vendor-specific conditional command.**

1. **One command when one does it.** The provider sends a single command when one command is the whole operation, such
   as `SET` with `NX` and `PX` for an occurrence, `GETDEL` for a take and `INCRBY` for a reservation.
2. **A script otherwise.** Each multi-step operation is one Lua script, run with `ScriptEvaluateAsync` and the script's
   text, which goes through the client's script cache and returns a `RedisResult` that a unit test builds with
   `RedisResult.Create`. `ScriptEvaluateRespAsync` saves allocations on large replies, which the scripts' short status
   replies aren't, and a test can't build its `RespResult`. Scripts are constant text shipped in the provider's assembly
   and written for this product. They are never generated or concatenated per call, and every variable part is an
   argument.
3. **Every key in `KEYS`.** A script touches only the keys it receives in `KEYS` and never derives a key name, so the
   client routes it and Garnet's transaction mode locks exactly its keys. Every key of one operation lies in one cluster
   slot, which the cluster record decides how to arrange.
4. **Check, then write.** A script reads and checks everything its outcome depends on before its first write, and
   returns an expected outcome, such as conflict or held, without writing. After its first write it calls only commands
   that can't fail on the arguments it has checked, because neither server rolls back a script's earlier writes when it
   fails.
5. **The server's clock.** A script that needs the current instant reads `TIME`, and every expiry it sets is a duration
   (`PX`, `PEXPIRE`), so expiry stays on the server's clock (ADR-1008).
6. **Bounded work.** A script's work is bounded by the contracts' limits: 64 conditions and writes, 16 increments, 10,000
   set members and 1,024 semaphore slots. No script loops over keys it hasn't received.
7. **Replies.** A script returns a status code first, then the outcome's fields. The seam of ADR-1022 parses replies by
   category so either protocol reads the same, and maps an error reply to a code by its first word after any repeated
   `ERR`, so Garnet's doubled prefix reads like Valkey's.
8. **Outcomes.** Every expected refusal is a status code, never an error. A script that ends with an error may have
   written before it, so the seam reports it with `OutcomeUnknown` true. An error the server returns before it runs the
   script, such as `NOPERM`, reports `OutcomeUnknown` false. A reply that never arrives leaves the outcome unknown, as
   ADR-1022 rule 4 says.
9. **Garnet.** The Garnet profile requires `--lua` and `--lua-transaction-mode`. The startup probe runs a script, which
   fails when Lua is off. Garnet doesn't report its transaction mode, so the profile states that requirement, and the
   conformance kit's concurrent runs are what show a server that misses it.
10. **ACL.** The provider's server user needs `EVAL`, `EVALSHA` and the commands its scripts call, and `SCRIPT` for the
    client's script cache: without it, every call sends the whole script with `EVAL`. The startup probe runs a script
    that calls each of them, so a missing permission fails the start as `access-denied`.

## Options considered

| Force | Lua scripts, one per operation | Functions (`FUNCTION LOAD`, `FCALL`) | `MULTI`/`EXEC` with `WATCH` | Conditional commands only (`SET … IFEQ`, `DELIFEQ`) |
| --- | --- | --- | --- | --- |
| F1 atomic and isolated | Valkey: blocks the server; Garnet: locks its keys in transaction mode | Valkey only | Yes, with retries on every conflicting write | One key at a time |
| F2 server-side logic | Yes | Yes | No: read, then queue, then retry | Value equality only |
| F3 server clock | `TIME` in the script | `TIME` in the function | The caller's reads | Expiry only |
| F4 keys known first | `KEYS` | Keys argument | Watched and queued keys | One key |
| F5 failure clarity | Check-then-write, one reply | Same | `EXEC` reply; a lost one is unknown | One reply |
| F6 operations | Nothing to deploy; the client resends scripts after a failover | Libraries to load, version and replace together across a rolling upgrade; Garnet lacks it | Nothing; but StackExchange.Redis doesn't expose `WATCH` on the multiplexer | Valkey 8.1/9.0 only; Garnet lacks both |

- **Lua scripts.** Chosen: it is the only option that meets F1 to F6 on both profiles. Its costs are Garnet's two
  startup settings (rule 9) and Lua code the product writes and tests.
- **Functions.** Rejected: Garnet doesn't support them. On Valkey, a library is one name per server, so two provider
  versions in a rolling upgrade would each replace the other's code. Persistence gains nothing, since the client
  reloads a script it finds missing.
- **`MULTI`/`EXEC` with `WATCH`.** Rejected: it can't branch on a value it reads, so a compare-and-swap, a counter batch
  or a transaction's else branch becomes read, watch, queue and retry. Every write takes the keyspace's revision, so
  writers would conflict and retry constantly. StackExchange.Redis's `ITransaction` conditions can't express the
  contracts' checks either.
- **Conditional commands only.** Rejected: they compare one key's value, while records compare a revision and write two
  keys, and Garnet has neither command. Where a single command already does a whole operation, rule 1 uses it.

## Consequences

- One code path serves Valkey and Garnet, and a profile differs only in its settings and its probe.
- Lua is product code: each script is reviewed, kept small and tested by the conformance kit against every profile,
  under concurrency and with faults injected.
- A host's Garnet must be started with `--lua --lua-transaction-mode`. Without transaction mode Garnet's scripts aren't
  isolated, and nothing at startup can tell.
- The cluster decision has to put every key of an operation in one slot, including the keyspace's revision counter and
  a session's liveness key.
- A notification or watch that a write publishes is part of that write's script, so the notification decision builds
  on this one.
- You chose wrong if the history checker finds a non-linearizable history that a script's isolation should have
  prevented, or if a managed Valkey service the product targets refuses `EVAL`. Then a record that supersedes this one
  weighs functions on Valkey or a profile without scripts.

## References

- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md),
  [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md),
  [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md),
  [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md),
  [ADR-1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md)
- Valkey documentation, read 2026-11-25: [Scripting with Lua](https://valkey.io/topics/eval-intro/) and
  [Functions](https://valkey.io/topics/functions-intro/)
- [Valkey CAS now built-in](https://www.fractolog.com/2025/11/valkey-cas-now-built-in/), in search results read
  2026-11-25: the versions that add `SET … IFEQ` and `DELIFEQ`
- Garnet documentation, read 2026-11-25: [API compatibility](https://microsoft.github.io/garnet/docs/commands/api-compatibility),
  [Scripting and functions](https://microsoft.github.io/garnet/docs/commands/scripting) and
  [Configuration](https://microsoft.github.io/garnet/docs/getting-started/configuration)
- StackExchange.Redis documentation, read 2026-11-25: [Scripting](https://seredis.dev/Scripting) and
  [Pipelines and Multiplexers](https://seredis.dev/PipelinesMultiplexers)
- A test run on 2026-11-25 with `valkey-cli` against `valkey/valkey:9` and `ghcr.io/microsoft/garnet` 2.2.14 containers:
  `CONFIG GET lua*`, a script that reads `TIME` and sets a key with `PX`, and a script that writes, then returns an error
- StackExchange.Redis 3.4.35's XML documentation of `CommandFlags.NoScriptCache` and `IDatabase.ScriptEvaluate`, read
  2026-11-25 from its package, and its [release notes](https://seredis.dev/ReleaseNotes), read 2026-11-25: the entry
  that adds `NoScriptCache`
- A test run on 2026-11-25 through StackExchange.Redis 3.4.35's public API against `valkey/valkey:9.2.0` and
  `ghcr.io/microsoft/garnet:2.2.13` containers: `ScriptEvaluateAsync` with a script's text, watched with `MONITOR`,
  before and after `SCRIPT FLUSH`, and as a user denied `SCRIPT`
