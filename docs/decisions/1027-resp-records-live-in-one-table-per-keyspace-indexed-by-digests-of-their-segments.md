# 1027. RESP records live in one table per keyspace, indexed by digests of their segments

- **Status:** Proposed
- **Date:** 2026-11-26
- **Deciders:** NCode Group
- **Amends:** 1023, 1025

## Context

The RESP provider keeps records and transactions
([ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)) under
earlier decisions that constrain where a record lives:

- **F1. Every key in `KEYS`.** A script touches only the keys it receives and never derives a key name
  ([ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
  rule 3), and those are the keys Garnet's transaction mode locks. A listing returns records the caller doesn't name,
  and an expiry removal deletes them.
- **F2. Listing from an index in the keyspace's slot**, never a scan
  ([ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md) rule 4),
  in pages whose continuation token holds at most 1,024 visible ASCII characters
  (`CoordinationLimits.MaxContinuationTokenLength`). A key holds up to 16 segments of up to 256 characters each, so its
  encoded form can run to tens of thousands of characters.
- **F3. Every change numbered and announced by its script**
  ([ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
  rule 1), a take included, which ADR-1023 rule 1 names as one `GETDEL`.
- **F4. Expiry through scripts**, with a server time to live as a backstop (ADR-1025 rule 7), and a revision greater
  for every later write in the keyspace (ADR-1014 rule 1).
- **F5. Prefixes by whole segments.** A prefix lists the keys whose leading segments equal its own, whatever their
  partition depth (`CoordinationKey.IsPrefixOf`).

How the servers order an index (read 2026-11-26; sources under References): a sorted set whose members all have the
same score is ordered byte by byte, and `ZRANGEBYLEX` reads a range of it between an inclusive (`[`) or exclusive
(`(`) bound and another, with `LIMIT`, on Valkey and on Garnet.

## Decision

**A keyspace's records live in three server keys beside its revision, and every record script receives all four. An
index of the records' members, each made of a digest of every key segment, lists a prefix as one lexicographic range,
so a continuation token stays short whatever a key's length.**

1. **The table.** `{keyspace}:records` is a hash with three fields per record, `r:`, `n:` and `v:` followed by the
   record's member, which hold its revision, its name and its value. `{keyspace}:record-index` is a sorted set of the
   members, all scored 0. `{keyspace}:record-deadlines` is a sorted set of the members of the records with a time to
   live, each scored with its deadline in milliseconds on the server's clock. `{keyspace}:revision` holds the
   keyspace's revision (ADR-1024 rule 2).
2. **Names and members.** A record's name is its partition depth, `:` and its encoded segments joined by `/`. Its
   member is, for each segment, the unpadded Base64Url form of the SHA-256 digest of the encoded segment followed by
   `/`, and last the partition depth: at most 706 characters. The members of the keys under a prefix are those that
   start with the prefix's digests, so a page is one range read, and its continuation token is the last member the
   page examined (rule 7).
3. **Every operation is a script.** Create, read, compare-and-swap, compare-and-delete, take, list and transaction
   each run as one script: a start that every record script shares, then the operation's own text, joined once when
   the provider loads them, so each script is constant text (ADR-1023 rule 2). A take is a script, not `GETDEL`, since
   it numbers and announces its delete; this amends ADR-1023 rule 1.
4. **Revisions.** A keyspace's revision is 1 before its first change, and each change sets it one higher. The key that
   holds it has no time to live, so no revision is given twice.
5. **Events.** Each change publishes on `{keyspace}:watch`, with `SPUBLISH` on a profile with sharded channels and
   `PUBLISH` otherwise (ADR-1025 rule 2): its revision, then a line for each record it wrote, `w` and the record's
   name, or deleted, `d` and the name, separated by line feeds. A change whose deletes find no record still publishes
   its revision, so the numbers stay consecutive.
6. **Expiry.** A record whose deadline is not after the server's current time is absent to every script. Every record
   script first deletes up to 64 such records, as one change. The backstop of ADR-1025 rule 7 applies to the table's
   three keys together: when every record has a time to live, they expire one hour after the latest deadline; when
   any record is persistent, they don't expire; when no record is left, they are deleted. This amends ADR-1025 rule 7,
   which gives each key a backstop of its own.
7. **Bounded listing.** A page examines at most its limit plus 64 members of the index, the number of expired records
   every record script deletes, and reads one more to tell whether any follow. A member whose record has expired but
   is not yet deleted counts toward that bound, so a page can hold fewer records than its limit, or none, and still
   carry a continuation token: the last member it examined. This amends ADR-1023 rule 6: a listing's work is bounded
   by the page size, 1,000 records, plus 64.

## Options considered

- **One table per keyspace.** Chosen: every script receives every key it touches, whichever records it reads or
  deletes (F1), a listing is a range read (F2), and a transaction over 64 records still receives four keys.
- **One server key per record.** Rejected: a listing and an expiry removal would touch keys no caller named, which
  ADR-1023 rule 3 rules out and Garnet's transaction mode doesn't lock.
- **An index of names.** Rejected: a name can pass the 1,024 characters of a continuation token, so a page's last
  record couldn't say where the next page starts.
- **An index in write order**, as the in-process provider keeps. Rejected: a prefix's records are spread over the
  whole keyspace in that order, so a page would scan it.

## Consequences

- A keyspace's records share three keys on one shard, which ADR-1024 already makes the unit of placement.
- Records bound to a session, and the periodic expiry removal of ADR-1025 rule 6, build on the same table.
- A listing's order is the digests' order, which ADR-1014 rule 3 leaves to the provider.
- Two keys whose segments have the same SHA-256 digests would share a record; finding such a pair is impractical.
- A listing over many expired records not yet deleted returns short or empty pages with a token, and a caller pages on
  until a page has none.
- You chose wrong if one keyspace's records grow past what a script can read in bounded time, or if a consumer needs
  listings in key order. Then a record that supersedes this one weighs per-record keys with a listing that receives
  them.

## References

- [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md),
  [ADR-1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md),
  [ADR-1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md)
- `src/NCode.Coordination.Abstractions/Operations/CoordinationLimits.cs` and
  `src/NCode.Coordination.Abstractions/Keys/CoordinationKey.cs`: the limits of a continuation token and of a key
- [Valkey: ZRANGEBYLEX](https://valkey.io/commands/zrangebylex/), read 2026-11-26: the order of members with equal
  scores and the bounds of a range
- [Garnet: API compatibility](https://microsoft.github.io/garnet/docs/commands/api-compatibility), read 2026-11-26:
  the hash, sorted-set, key and Pub/Sub commands the record scripts call
- [.NET: Base64Url](https://learn.microsoft.com/dotnet/api/system.buffers.text.base64url), read 2026-11-26
- A test run on 2026-11-26 through `valkey-cli` against `valkey/valkey:9.2.0` and `ghcr.io/microsoft/garnet:2.2.13`
  started with `--lua --lua-transaction-mode true`: the record scripts, their replies and events, the removal of 64
  expired records and the backstop, the same on both
