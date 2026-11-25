# 1013. Ownership is granted with a fencing token and an owner token, and bound to a session or a time to live

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

[ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)
defines sessions, fencing tokens, owner tokens and claims, and they need operation shapes.

etcd grants a lease with a time to live in seconds that the server may change, keeps it alive over a stream, and deletes
the keys attached to it when it ends. Its lock and election services return a key whose creation revision guards writes
(read 2026-11-25). A Consul session has a time to live of 10 to 86,400 seconds, a renewal may return a longer one, a key
acquired by a session counts its acquisitions in `LockIndex`, and destroying a session is idempotent (read 2026-11-25).

## Decision

**Ownership is a grant that carries an owner token and a fencing token, and it is bound to a session or to a time to
live. Acquisition never blocks.**

1. **Sessions.** Opening one in a keyspace with a time to live returns a session id (the keyspace and an opaque token,
   such as an etcd lease ID or a Consul session UUID) and the granted time to live, never shorter than requested.
   Keep-alive returns renewed, with the granted time to live, or ended. Close is idempotent. A wait completes when the
   session ends by close or expiry. Memberships, leases, semaphore slots, elections and records can be bound to a session
   and end with it. A provider rounds a time to live up to its server's unit and minimum and refuses one above its
   server's maximum as `limit-exceeded`.
2. **Membership.** A group is a key. A member is its session, a node id, an address and metadata. Joining returns joined,
   node id taken (another live session holds it) or session ended, and a session that joins again replaces its own
   entry. Reading and watching return a view of the group's members at a revision. The watch yields the current view
   first and then views at greater revisions, possibly skipping some, since membership is state, not a log.
3. **Grants.** Lease terms are bound to a session or to a time to live. Every grant returns an owner token, which renewal
   and release require, and a fencing token greater than every earlier grant of the resource (etcd: the lock key's
   creation revision; Consul: the key's `LockIndex`). A lease bound to a session lives as its session does, so renewing
   it is an argument error.
4. **Leases.** Try-acquire returns acquired with the grant, held or session ended. Acquiring again with the same terms'
   session returns the existing grant. Renew and release return whether the grant still held. Wait-for-release completes
   once the resource has no holder. Acquisition never blocks, so waiting and fairness belong to the recipes.
5. **Semaphores.** The capacity, 1 to 1,024, is named on every acquire. Each grant has a slot index below the capacity
   with its own fencing token. The outcomes are acquired, full, capacity conflict (holders use another capacity) and
   session ended. Acquiring again with the session of a slot already held returns that grant.
6. **Elections.** Try-lead with a value up to 65,536 bytes, such as the leader's address, returns elected, led by another
   or session ended. The term is the leader's fencing token. Read and watch give the role's leader or none, the watch
   current first and then later terms. Resign returns whether it still led. Trying to lead again with the session of
   the leadership returns that leadership.
7. **Claims.** Try-claim on a name, a time window and a time to live returns claimed or taken, at most one per name and
   window, with no release and no token. The time to live runs to the window's end plus the caller's skew allowance
   ([ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
   rule 5).

## Options considered

- **One grant type for leases, slots and terms.** Rejected: slot, term and value differ.
- **A blocking acquire.** Rejected: it hides fairness and the race between cancellation and a grant.
- **The fencing token as the release credential.** Rejected: anyone who sees a token could release the lease.

## Consequences

- A holder keeps its owner token.
- A lock recipe waits and retries.
- A lost session releases everything bound to it at once.

## References

- [ADR-1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- Burrows, "The Chubby lock service for loosely-coupled distributed systems", OSDI 2006
- Kleppmann, "Designing Data-Intensive Applications", O'Reilly 2017, chapter 8
- etcd v3.5, "etcd API" and "API concurrency reference" (read 2026-11-25)
- Consul HTTP API, session and KV pages (read 2026-11-25)
- [Provenance log](../provenance.md)
