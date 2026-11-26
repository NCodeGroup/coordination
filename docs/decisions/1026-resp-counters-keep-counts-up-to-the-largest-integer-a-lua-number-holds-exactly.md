# 1026. RESP counters keep counts up to the largest integer a Lua number holds exactly

- **Status:** Proposed
- **Date:** 2026-11-26
- **Deciders:** NCode Group

## Context

A counter's count is a 64-bit integer in the contract (`ICounterService`), and the in-process provider refuses an
increment that would pass 2^63 − 1 as `limit-exceeded`. The RESP provider runs every counter increment, and every
batch of up to 16, as one Lua script that reads each counter, checks each increment against its limit and stops after
the first that exceeds it, all before its first write
([ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)
rule 4, [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
rule 4).

Valkey runs scripts in Lua 5.1, whose one number type is a double: integers above 2^53 − 1 (9,007,199,254,740,991)
lose precision. An integer reply becomes a Lua number in a script, and a Lua number a script returns becomes an
integer reply without its fraction (Valkey's Lua API reference, read 2026-11-26). Past 2^53 − 1, a script can't add a
count and an amount, or compare a count with a limit, without arithmetic of its own on decimal strings. `INCRBY` adds
exactly on the server, but it writes, so a check after it comes too late for rule 4 of ADR-1023.

## Decision

**A RESP counter keeps counts up to 2^53 − 1. An increment that would pass it fails as `limit-exceeded`, with the
outcome known not to have applied, and the batch that holds it applies none of its increments.**

1. The script computes each count as a Lua number, and refuses the batch, before its first write, when an amount is
   greater than 2^53 − 1 less the count before it.
2. It writes each applied increment with `INCRBY` and the amount as the caller sent it, so the server stores the exact
   sum.
3. Limits keep their whole range: a count the script keeps never exceeds a limit of 2^53 or more, and compares exactly
   with a smaller one.
4. Sequences hand out their numbers with `INCR` and `INCRBY` alone, in the server's 64-bit arithmetic, and keep their
   whole range.

## Options considered

- **Counts up to 2^53 − 1.** Chosen: every count and comparison in the script is exact, and the script stays short.
  Counters count requests and attempts in windows, far below the bound.
- **Decimal arithmetic in the script.** Rejected: adding and comparing counts as digit strings keeps the 64-bit range,
  at the cost of Lua code that runs on every increment, for counts no rate limit reaches.
- **Increment first, check after.** Rejected: a batch that finds an increment past the largest count after writing the
  earlier ones leaves them applied, which ADR-1023 rule 4 rules out.

## Consequences

- A consumer that counts past 2^53 − 1 on the RESP provider gets `limit-exceeded`, where the in-process provider counts
  on to 2^63 − 1.
- The provider's readme states the largest count.
- You chose wrong if a consumer needs counts past 2^53 − 1. Then a record that supersedes this one weighs decimal
  arithmetic in the script, or a profile whose Lua has 64-bit integers.

## References

- [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md),
  [ADR-1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md)
- `src/NCode.Coordination.Abstractions/Counters/ICounterService.cs`, and
  `src/NCode.Coordination/InProcess/InProcessCounterService.cs`
- [Valkey: Lua API reference](https://valkey.io/topics/lua-api/), read 2026-11-26: the embedded Lua 5.1 and the
  conversions between Lua numbers and integer replies
- A test run on 2026-11-26 through `valkey-cli` against `valkey/valkey:9.2.0` and `ghcr.io/microsoft/garnet:2.2.13`
  started with `--lua --lua-transaction-mode true`: the counter script with a count of 2^53 − 1, and an increment past
  it
