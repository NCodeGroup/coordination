# 1004. A node holds a session that binds its leases and membership, and every lease grant carries a fencing token

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

A node that crashes holds whatever it held until each item expires on its own, each with a lifetime its feature had to
guess. A node that pauses (a long garbage collection, a stalled virtual machine) can resume believing it holds a lease
that has expired and passed to another, and write as its owner; a time bound alone cannot prevent that, because the
pause can happen between the check and the write.

## Decision

**A node opens a session that it keeps alive; leases, ephemeral records and membership can be bound to the session
and end when it ends; and every lease grant carries a fencing token that increases with each grant of the resource.**

1. **Sessions.** A session has a time to live, renewed by the node while it runs. A graceful shutdown closes it,
   releasing everything bound to it at once; a crash lets it expire.
2. **Fencing tokens.** A grant returns a token greater than any earlier grant of the same resource. A resource that
   takes writes from lease holders accepts a write only with a token at least as great as the greatest it has seen.
3. **Owner tokens.** A lease not bound to a session is released by the owner token its grant returned, so a holder
   never releases a lease another holder took after its own expired.
4. **Claims are separate.** A per-window claim ("run the pass of this interval") is a claim on the window's name, which
   expires with the window, not a lease whose lifetime approximates it.

## Options considered

- **Leases as expiring keys.** Rejected: without a fencing token, a paused holder's write is indistinguishable from the
  current holder's.
- **Time-bound leases with conservative lifetimes.** Rejected: no lifetime bounds a pause.

## Consequences

- A host's shutdown releases its leases at once, so a successor does not wait out their lifetimes.
- A provider whose server cannot issue increasing tokens across failover declares fencing only until failover.

## References

- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006 (sequencers)
- Kleppmann, "Designing Data-Intensive Applications", O'Reilly 2017, chapter 8 (fencing tokens)
- Hunt et al., "ZooKeeper: Wait-free coordination for Internet-scale systems", USENIX ATC 2010 (sessions, ephemeral
  nodes)
