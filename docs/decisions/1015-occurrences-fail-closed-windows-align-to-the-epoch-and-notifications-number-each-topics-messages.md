# 1015. Occurrences fail closed, windows align to the epoch, and notifications number each topic's messages

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

The first consumer needs replay checks, indexes, rate limits and change signals (the design's Context table), and each
needs a small primitive with a shape every provider can supply.

A Consul KV key has no expiry of its own, and Consul's user events spread by gossip with no ordering and no promise of
delivery (read 2026-11-25). An etcd key's version grows by one with each write (read 2026-11-25).

## Decision

**An occurrence never reports success when the provider cannot answer. A time window starts at a multiple of its length
from the Unix epoch. A notification topic numbers its messages.**

1. **Occurrences.** Try-record a key with a time to live returns recorded or duplicate, and throws when the provider
   cannot answer, never answering recorded. A provider that cannot expire a key on its server measures expiry on the
   caller's clock and declares `expiry.caller-clock`, as a Consul provider would.
2. **Sets.** Members are key segments (text, number or UUID), each with its own time to live. Add returns whether the
   member was absent, and a present member's expiry is replaced. Remove returns whether the member was present. List
   returns the live members in no order. Clear empties the set. A set holds at most 10,000 members, beyond which add
   fails as `limit-exceeded`.
3. **Time windows.** A window's start is a multiple of its length from the Unix epoch, in UTC and whole milliseconds.
   The window containing an instant is computed on the caller's clock. Its key segment is its start in Unix
   milliseconds, and its time to live runs to its end plus a skew allowance
   ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
   rule 5). Claims and counters name their window this way.
4. **Counters.** Increment by an amount of at least 1 with a time to live returns the count after it, and the time to
   live applies when the increment creates the counter, so a window's counter ends with its window. Read returns 0 when
   the counter is absent. Reset deletes it. A batch of 1 to 16 increments, each with a limit, is atomic: it applies them
   in order and stops after the first whose count then exceeds its limit. That increment stays applied with the earlier
   ones, and later ones are not applied. The result gives the count after each applied increment and the index of the
   one that exceeded.
5. **Sequences.** Next returns one number, and reserve returns 1 to 65,536 consecutive numbers. Numbers start at 1, are
   unique and increase across all callers, and never reset. A reserved number left unused is never handed out again.
6. **Notifications.** Publish sends a payload of at most 4,096 bytes on a topic key. A subscription streams messages with
   consecutive per-topic sequence numbers, each at most once, and resync signals that carry the last sequence
   delivered, or 0 when none. The stream starts with a resync and resyncs on a gap or reconnect. A provider that cannot
   number a topic declares `delivery.at-most-once`, as a provider over Consul's user events would. One over etcd can
   number a topic from its key's version.

## Options considered

- **An all-or-nothing counter batch.** Rejected: in a fixed window an attempt over a limit still counts, and the
  design's batch stops at the first counter over its limit.
- **Sets of opaque bytes.** Rejected: key segments are already typed, validated and encoded by every provider.
- **Notification payloads as large as records.** Rejected: a notification is a signal
  ([ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md)
  rule 3).

## Consequences

- A replay guard treats every failure as a refusal.
- A rate limiter computes the window, then increments.
- A subscriber rereads on every resync.

## References

- [ADR-1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- [ADR-1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)
- [The design](../designs/coordination.md), its Context
- etcd v3.5, "etcd API" (<https://etcd.io/docs/v3.5/learning/api/>), read 2026-11-25
- Consul HTTP API, [KV](https://developer.hashicorp.com/consul/api-docs/kv) and
  [events](https://developer.hashicorp.com/consul/api-docs/event), read 2026-11-25
