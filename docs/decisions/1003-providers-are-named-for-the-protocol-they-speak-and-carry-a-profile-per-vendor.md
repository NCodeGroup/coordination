# 1003. Providers are named for the protocol they speak and carry a profile per vendor

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

Several servers speak one protocol and differ in what they keep: Redis, Valkey and Garnet all speak RESP, yet differ
in failover, scripting isolation and supported commands. A service chooses a server it already runs, and a library
that names its provider after one vendor either excludes the others or misstates their guarantees. Messaging libraries
solve the same shape with one programming model and one transport package per broker.

## Decision

**A provider package is named for the protocol it speaks (`NCode.Coordination.Resp`, `.PostgreSql`, `.Etcd`,
`.Consul`), and carries a profile per vendor that states that vendor's guarantees, settings and requirements.**

1. A profile names the vendor, the guarantees per primitive, and the server settings those guarantees need (Garnet's
   scripting must run as a transaction, for example).
2. At startup the provider probes the server: it identifies the vendor and version, and, where a guarantee depends on
   a server setting, verifies it, failing when it does not hold.
3. Each provider has its own configurator on the composition builder, as a messaging transport has.
4. Profiles are an open vocabulary: a consumer registers a profile for a vendor the provider does not ship.

## Options considered

- **One package per vendor.** Rejected: the protocol code is the same, and the packages would differ only in a table.
- **One package per protocol with no profiles.** Rejected: the guarantees would be the weakest vendor's or a guess.

## Consequences

- Supporting another vendor of a supported protocol is a profile and a conformance run, not a package.
- A provider's startup probe is part of its contract and its tests.

## References

- [Design: Packages](../designs/coordination.md#packages)
- [Provenance](../provenance.md): the Garnet documentation's compatibility and scripting pages
