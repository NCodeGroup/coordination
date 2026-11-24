# 1006. Every provider passes one conformance kit that checks recorded histories against each guarantee

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

A provider's correctness depends on its server's behavior under concurrency and failure, which a mock cannot show. A
script that is atomic on one server and interleaved on another passes every unit test of the code that sends it. The
guarantees each provider declares are claims; only a test against a real server, under concurrent clients and
injected faults, can support them.

## Decision

**`NCode.Coordination.Testing` holds one conformance kit, which every provider runs against a real server for each
primitive and guarantee it declares, and which checks the recorded history of concurrent operations against the
guarantee.**

1. **Contract tests.** Each primitive's operations, edge cases and expiry, the same for every provider.
2. **History checking.** Concurrent clients record each operation's invocation and response; a checker verifies that
   the history is linearizable for a linearizable guarantee, and within the declared weaker model otherwise.
3. **Faults.** Where a provider declares behavior under failure, the kit injects it: a dropped connection, a paused
   client, a server restart, a failover.
4. **Every provider, every guarantee.** A provider ships only the guarantees the kit verifies for it; the in-process
   provider runs the kit too.
5. **Consumers can run it.** A consumer runs the kit against its own server configuration before trusting it.

## Options considered

- **Unit tests with mocked servers.** Rejected: they test what the code sends, not what the server does with it.
- **Tests per provider, written separately.** Rejected: providers drift, and a guarantee means something different in
  each.

## Consequences

- Adding a provider or a profile is mostly a conformance run.
- The kit needs container or embedded servers in CI, and its fault tests take longer than unit tests.

## References

- Herlihy, Wing, "Linearizability: A correctness condition for concurrent objects", TOPLAS 1990
- Wing, Gong, "Testing and verifying concurrent objects", Journal of Parallel and Distributed Computing 1993
- Lowe, "Testing for linearizability", Concurrency and Computation: Practice and Experience 2017
- [Jepsen analyses](https://jepsen.io/analyses), as published reports of the method
