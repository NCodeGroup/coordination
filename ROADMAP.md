# Roadmap

What NCode.Coordination intends to do over the next year. Open issues hold the detail and the current state; this page
is the direction. It is reviewed at least once a year and when a release changes it.

[//]: # "Maintainers: replace the prompts with the project's own plans, and review this page at least once a year."

## Scope

NCode.Coordination covers what [README.md](README.md) describes. A feature outside that starts as an issue, where the
maintainers say whether it fits.

## Next Twelve Months

[//]: # "Maintainers: list what the project plans to do in the next twelve months, one bullet each."

- **Model.** The abstractions, the in-process provider and the conformance kit with its history checker
  ([design](docs/designs/coordination.md#rollout-and-phases)).
- **RESP.** The RESP provider with Valkey and Garnet profiles, covering every need of NCode.Identity, the first
  consumer.
- **PostgreSQL.** A provider that needs no infrastructure beyond the database a service already runs, in six plans,
  [#41](https://github.com/NCodeGroup/coordination/issues/41) to
  [#46](https://github.com/NCodeGroup/coordination/issues/46), built on
  [ADR-1031](docs/decisions/1031-the-postgresql-provider-speaks-through-npgsql-and-can-share-the-hosts-data-source.md)
  to
  [ADR-1034](docs/decisions/1034-postgresql-notifications-and-watches-are-numbered-in-the-writing-transaction-kept-in-a-bounded-log-and-woken-by-notify.md)
  ([design](docs/designs/coordination.md#rollout-and-phases)).
- **First-consumer adoption.** NCode.Identity moves its shared state onto NCode.Coordination, tracked in the
  cross-repository epic [NCodeGroup/.github#4](https://github.com/NCodeGroup/.github/issues/4).
- **etcd and Consul.** Providers that keep sessions, leases and watches linearizable across failover.
- **Supply chain.** Keep external inputs pinned and current
  ([ADR-0008](docs/decisions/project-template/0008-external-inputs-are-pinned-and-kept-current.md)),
  and raise the OpenSSF Scorecard score where the repository controls it
  ([ADR-0020](docs/decisions/project-template/0020-github-projects-are-scored-by-openssf-scorecard.md)).
- **OpenSSF Best Practices.** Reach the silver level, which needs a second owner for continuity
  ([GOVERNANCE.md](GOVERNANCE.md#continuity), [docs/best-practices-badge.md](docs/best-practices-badge.md)).

## Not Planned

[//]: # "Maintainers: list what the project rules out, and why."

- **A message broker.** Durable queues, routing and consumer groups belong to a broker or a job dispatcher
  ([ADR-1005](docs/decisions/1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md)).
- **A database, a cache or a storage server.** Providers run over existing servers; a native coordination server is
  considered only for a need no provider meets
  ([ADR-1000](docs/decisions/1000-ncode-coordination-is-a-general-coordination-library-for-the-nodes-of-a-cluster.md)).
- **Actors and RPC between nodes.** Membership and singletons are in scope; an actor programming model is not.
