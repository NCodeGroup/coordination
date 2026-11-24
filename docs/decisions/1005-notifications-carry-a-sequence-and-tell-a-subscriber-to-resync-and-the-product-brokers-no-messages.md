# 1005. Notifications carry a sequence and tell a subscriber to resync, and the product brokers no messages

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

Nodes cache what they read and need to hear when it changes. A fire-and-forget broadcast loses messages silently: a
subscriber cannot tell a quiet topic from one whose messages it missed, so it either revalidates on every reconnect or
trusts a cache that may be stale. A durable broker would deliver every message, but makes the coordination layer a
system of record and brings queues, consumer groups and retries that coordination does not need.

## Decision

**A notification is a coordination signal: delivered to every subscriber at most once, numbered by a per-topic
sequence, and followed by an explicit resync signal whenever a subscriber may have missed one. The product carries no
durable queues, routing or consumer groups.**

1. **Sequence.** A topic's notifications carry consecutive sequence numbers. A subscriber that sees a gap, subscribes,
   or reconnects receives a resync signal, and its handler rereads what the topic covers.
2. **Watches share the shape.** A watch on records under a key or prefix is a notification topic whose sequence is the
   records' revision, resumable from a revision where the provider keeps history.
3. **Signals, not data.** A notification names what changed; the state is read from where it lives.
4. **Durable work belongs elsewhere.** Work that must run is a job for a dispatcher or a message for a broker;
   coordination supplies only the claim or lease that keeps two nodes from running it.

## Options considered

- **Best-effort broadcast with a reconnect hook.** Rejected: a message lost without a reconnect is never detected.
- **A broker abstraction over brokers.** Rejected: it duplicates messaging libraries and makes coordination state
  durable business data.

## Consequences

- A cache kept by notifications is stale for at most the time to the next notification or resync, never indefinitely.
- A provider whose server cannot number notifications declares at-most-once delivery without gap detection, and a
  feature that needs detection refuses it.

## References

- [Design: Primitives](../designs/coordination.md#primitives)
- [Provenance](../provenance.md): Stalwart Keep's design document, on per-subscription sequence numbers
