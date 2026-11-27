# 1028. RESP ownership state lives in fixed keys per keyspace, bound to sessions through one index

- **Status:** Proposed (amended by 1029)
- **Date:** 2026-11-26
- **Deciders:** NCode Group
- **Amends:** 1025, 1027

## Context

[ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md)
gives sessions, membership, leases, semaphores, elections and claims their operations, and a session's end ends
everything bound to it, records included. Earlier RESP decisions constrain where that state lives:

- **F1. Every key in `KEYS`.** A script touches only the keys it receives and never derives a key name
  ([ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
  rule 3), and those are the keys Garnet's transaction mode locks.
- **F2. Expiry through scripts.** State that ends with a session or a time to live expires through scripts, with a
  server time to live as a backstop
  ([ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
  rule 7).
- **F3. One slot per keyspace**, with its revision beside it
  ([ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md)).

A key per resource or per session leaves a session's close keys its script never received: the session's leases, its
slots, its node and its records are all named by the session, not by the caller. Scanning for them derives key names
and costs as much as the keyspace holds.

## Decision

**A keyspace's ownership state lives in eight keys beside its record table, and every keyspace script, record scripts
included, receives all twelve. One index binds each session to what it holds, and every script first ends expired
sessions and grants.**

1. **The keys and their layout.** Every keyspace script receives the same twelve keys, in this order, each
   `RespKeys.ForKeyspace(keyspace, <kind>)`: `revision`, `records`, `record-index`, `record-deadlines`, `sessions`,
   `session-deadlines`, `session-bindings`, `grants`, `grant-deadlines`, `fencing`, `members`, `member-fields`. A
   record script receives twelve keys, not four; this amends ADR-1027. The layout of the eight ownership keys:
   - `sessions` is a hash of `<session>` to its time to live in milliseconds.
   - `session-deadlines` is a sorted set of sessions scored with their deadline; the score 0 marks a session that has
     ended and is not yet cleaned.
   - `session-bindings` is a sorted set, all scored 0, of `<session> <kind> <item>`: kind `g` for a grant field, `r`
     for a record member and `m` for `<group> <node>`.
   - `grants` is a hash with the fields `l <name>` (lease), `e <name>` (leadership), `v <name>` (leadership value),
     `s <slot> <name>` (semaphore slot, 0-based) and `c <name>` (semaphore capacity). Each holder line is
     `<owner> <fencing> <session or ->`.
   - `grant-deadlines` is a sorted set of the grant fields bound to a time to live, scored with their deadline.
   - `fencing` is a hash of the counters `l <name>`, `e <name>` and `s <name>`.
   - `members` is a sorted set, all scored 0, of `<group> <node>`.
   - `member-fields` is a hash with `s <group> <node>` (session), `a <group> <node>` (address), `m <group> <node>`
     (metadata) and `n <group> <session>` (the session's node in the group).

   `<name>` is `RespKeys.Name(key)` and `<group>` is `RespKeys.Member(group)`.
2. **Sessions.** A session id is `s-` and 32 lowercase hex digits, 128 random bits from `RandomNumberGenerator`
   behind the internal `IRespTokens`, so no clock is read and a test can substitute the source. The session's time to
   live is stored in `sessions`. Anything bound to a session is absent once the session is not live: its entry in
   `session-deadlines` is missing or not after the server's current time. A session's end removes at most 64 bindings
   per script, and every keyspace script first ends up to 16 expired sessions within that budget, so a script's work
   stays bounded (ADR-1023 rule 6).
3. **Grants.** Each grant has its own owner token, `o-` and 32 lowercase hex digits, 128 random bits through
   `IRespTokens`; the session id is never an owner token. A grant bound to a time to live is listed in
   `grant-deadlines`, and every keyspace script first drops up to 64 lapsed grants. The `fencing` counters never
   expire, so no fencing token is given twice. A semaphore acquisition scans at most the semaphore's capacity of
   slots.
4. **Records bound to a session.** A record bound to a session has a fourth field, `s:<member>`, in `records`, and
   a binding of kind `r` in `session-bindings`. It is absent once its session is not live. A session's end deletes
   the records bound to it, as one change that takes the keyspace's next revision and publishes it with a `d` line
   for each record.
5. **Membership.** A node joins a group with its session, its address and its metadata. A membership change, and a
   session's end that removes members, take the keyspace's next revision and publish it through the prelude's
   `publish`; a membership-only change publishes the revision alone, so record watches stay consecutive and match no
   key. This amends ADR-1025 rule 1, whose event carries every key the change wrote or deleted. Metadata travels as
   length-prefixed text, `<name length>:<name><value length>:<value>` per entry in ordinal order of names, encoded and
   decoded in C# (`RespMembershipMetadata`); the address is its own field, so a colon, `|` or `=` round-trips.
6. **Expiry and backstop.** State past its deadline is absent to every script, and cleanup is incremental by the
   budgets of rules 2 and 3. The ownership keys other than `fencing` get a backstop (ADR-1025 rule 7): `PEXPIRE` to
   one hour past the latest session or grant deadline, or `DEL` when no session and no grant deadline is left.
7. **Claims.** A claim is one `SET <key> 1 NX PX <ms>` on `RespKeys.ForKey(<name + window segment>, "claim")`, as an
   occurrence is (ADR-1023 rule 1), with the caller's time to live, which already carries its skew allowance
   ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
   rule 5). No claim script exists.
8. **Waits and watches.** A wait for a session's end, a lease release or a semaphore release, and a watch of an
   election or of membership, poll the server every 100 ms until the condition holds or the caller cancels. They never
   return as though the condition held. A watch yields the current view first, then each view whose term (election) or
   revision (membership) differs. A membership watch reads only the keyspace's revision on each poll and lists the
   group again only once the revision has moved, so an idle group costs one small script per poll whatever its size.

## Options considered

- **Fixed keys per keyspace, bound through one index.** Chosen: every script receives every key it touches (F1), a
  session's end reaches its grants, records and members in one script, and the work per script is bounded by rules 2
  and 3.
- **A key per resource and per session.** Rejected: a close touches keys it never received, which ADR-1023 rule 3
  rules out and Garnet's transaction mode doesn't lock.
- **Scanning the keyspace on close.** Rejected: the cost grows with the keyspace, it derives key names, and an
  expiry never runs it.
- **The session id as the owner token.** Rejected: membership shows the session id to every reader, so any reader
  could present it as an owner token, and two time-to-live grants in one millisecond would share a token of the form
  `ttl-<ms>`.
- **A claim script with the provider's own skew.** Rejected: the caller's time to live already carries the allowance
  (ADR-1008 rule 5), so a second one counts it twice.

## Consequences

- Every keyspace script receives twelve keys and may end expired sessions and drop lapsed grants first, within its
  budgets.
- A session holding more than 64 entries is removed over several scripts, its entries absent meanwhile.
- A session's end is one numbered change when it removes records or members, so watches stay consecutive.
- Waits and watches read the server every 100 ms while they run, until they subscribe to the keyspace's watch channel.
- A keyspace's ownership state shares one shard with its records, which ADR-1024 already makes the unit of placement.
- You chose wrong if one keyspace's sessions or grants outgrow what a script scans in bounded time. Then a record
  that supersedes this one weighs a key per resource with a close that receives them.

## References

- [ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md),
  [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md),
  [ADR-1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md),
  [ADR-1027](1027-resp-records-live-in-one-table-per-keyspace-indexed-by-digests-of-their-segments.md)
- The conformance suites of NCode.Coordination.Testing run against `valkey/valkey:9.2.0` and
  `ghcr.io/microsoft/garnet:2.2.13` started with `--lua --lua-transaction-mode true`, as
  `tests/NCode.Coordination.Resp.Tests/Servers/RespServers.cs` pins them
