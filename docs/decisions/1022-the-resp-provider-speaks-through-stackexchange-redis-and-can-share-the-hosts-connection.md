# 1022. The RESP provider speaks through StackExchange.Redis, and can share the host's connection

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

`NCode.Coordination.Resp` carries a profile per vendor
([ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)), and the first
stable release depends on it ([ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md)).
Valkey is the server the product targets first, and Garnet the other profile of the RESP phase
([Design: Rollout and Phases](../designs/coordination.md#rollout-and-phases)). Redis speaks the same protocol, and its
profile can follow as a further vendor profile.
Every later RESP decision, from atomic operations to cluster slots to notifications, is built on the client that carries
its commands. The client has to meet these forces:

- **F1. Every profile's server.** Valkey first, then Garnet, standalone and in cluster mode, with TLS and ACL users,
  and Redis through the same protocol.
- **F2. Every host.** Each runtime identifier a .NET host image targets, Alpine's musl and arm64 included, with no
  native library to ship.
- **F3. Originality.** A permissive license on the package and every transitive dependency, which
  `build/Test-Licenses.ps1` checks ([ADR-1009](1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md)),
  used through its public API and documentation only ([ORIGINALITY.md](../../ORIGINALITY.md)).
- **F4. Failures that say whether they took effect.** A `CoordinationException`'s `OutcomeUnknown` is false only when
  the provider knows the request never reached the server
  ([ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
  rule 2), and the history checker relies on it.
- **F5. The commands the primitives need.** Scripts, Pub/Sub that resubscribes after a reconnect, cluster routing by
  hash slot, and arbitrary commands for the startup probe that identifies the vendor and checks its settings
  (ADR-1003 rule 2).
- **F6. The host's own connection.** A host that already talks to the server, such as NCode.Identity through its Valkey
  adapter or an Aspire app through `AddRedisClient`, can share that connection instead of opening another.
- **F7. Cost.** What the client costs to build, keep current and keep correct across vendors and protocol versions.
- **F8. One client across the organization's code.** NCode Group's products use a single RESP client, chosen for the
  long term. On .NET, Microsoft's packages for the protocol build on StackExchange.Redis: at 11.0.0,
  `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`,
  `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` and `Microsoft.AspNetCore.SignalR.StackExchangeRedis` each
  depend on it, as does `Aspire.StackExchange.Redis` 17.0.0. A service that uses any of them already runs it.

The candidates (read 2026-11-25 from nuget.org and each project's documentation; sources under References):

- **StackExchange.Redis** 3.4.35, published 2026-11-12, MIT. It targets `net10.0`, `net8.0`, .NET Standard 2.0 and .NET
  Framework, and on `net10.0` depends on `RESPite` 3.4.35 (MIT), `System.IO.Hashing` (MIT) and
  `Microsoft.Extensions.Logging.Abstractions` (MIT). It is fully managed. It multiplexes callers over one connection
  per server and keeps a separate connection for Pub/Sub. It negotiates RESP3 where the server offers it, and RESP2
  otherwise. It runs scripts and arbitrary commands, and routes cluster commands by hash slot. Its `BacklogPolicy`
  decides whether commands queue while a connection is down. It never offers blocking commands, so one caller can't
  stall the multiplexer. A cancelled or timed-out asynchronous call may still be written to the server after the caller
  stops waiting. Garnet's documentation names it as the client Garnet is tested with most, and NCode.Identity's Valkey
  adapter uses it. Aspire's Valkey, Garnet and Redis integrations register its `IConnectionMultiplexer`. Its package is
  5.7 MB of managed code, with about 1.36 billion downloads. A 4.0 line is in alpha.
- **Valkey.Glide** 1.2.0, published 2026-11-17, Apache-2.0. It is the Valkey project's official client for C#, the only
  C# client valkey.io lists, and Google's Memorystore for Valkey documents it. Its 1.0.0 was published 2026-04-18, and
  it has about 343,000 downloads. It targets `net8.0`, over a Rust core loaded as a native library, in a 43.0 MB package
  with builds for glibc Linux, macOS and Windows on x64 and arm64. Its documentation lists Valkey 7.2 to 9.2 and Redis
  6.2 to 7.2, and doesn't list Garnet. It has no musl build, such as for Alpine, for C#. It adds availability-zone
  affinity, cloud IAM authentication and Valkey 9's commands, resubscribes Pub/Sub after topology changes, and supports
  blocking commands. Its API copies StackExchange.Redis's shape in its own types, which Microsoft's packages of F8 don't
  accept. In a test run on 2026-11-25, its connect, `SET NX` with an expiry, `INCR`, a Lua script, `INFO` and Pub/Sub
  worked against Valkey 9. Against Garnet 2.2.14 everything worked except Pub/Sub, whose subscription timed out over
  RESP2 and RESP3; StackExchange.Redis 3.4.35's Pub/Sub worked against the same server.
- **A client of the product's own**, written from the RESP2 and RESP3 specifications over `System.IO.Pipelines` and
  `SslStream`.

## Decision

**`NCode.Coordination.Resp` talks to its server through StackExchange.Redis, used only through its public API. A host
either gives the provider connection settings, and the provider owns its connection, or hands it the host's
`IConnectionMultiplexer`. The client's types stay out of every contract except the provider's configurator.**

1. **The package.** `NCode.Coordination.Resp` references StackExchange.Redis at the newest stable version past the
   dependency cooldown, 3.4.35 when this record was written. Dependabot proposes later versions, and a new major
   version, such as 4.0, is a planned migration.
2. **The connection.** The provider's configurator takes either connection settings, from which the provider creates,
   owns and disposes one multiplexer, or a factory that returns the host's `IConnectionMultiplexer`, which the provider
   uses and never disposes. `NCode.Coordination.Abstractions` and `NCode.Coordination` reference no client type.
3. **One internal seam.** The primitives reach the server through one internal layer that runs scripts and commands,
   subscribes to channels and classifies failures. No primitive calls the client directly. Changing the client changes
   this layer, not the primitives.
4. **Failures.** The seam turns the client's exceptions into `CoordinationException`s:
   - `OutcomeUnknown` is false only when the client reports that the command was never written, such as a connection
     failure while it waited to be sent.
   - A timeout and a dropped connection after the command was written leave the outcome unknown, and so does a command
     still queued in the backlog when its caller stops waiting.
   - A cancellation of the caller's token throws `OperationCanceledException`, as ADR-1011 rule 4 says of every
     operation, and leaves the outcome of a command already handed to the client unknown.
   - A server error reply maps to a code by its error prefix, such as `NOPERM` to `access-denied`, never by its message
     text.
   - A test against a real server with injected faults proves each mapping.
5. **No blocking commands.** Waits and streams, including notifications and watches, use Pub/Sub to wake and
   non-blocking reads to catch up. They never block a multiplexed connection, which the client refuses to do anyway.
6. **Either protocol.** The seam parses script and arbitrary-command replies in RESP2 and in RESP3, whichever the client
   negotiates. The conformance runs cover each profile in the protocol it negotiates.
7. **The probe.** The startup probe of ADR-1003 identifies the vendor, version and settings with arbitrary commands
   through the seam. A shared multiplexer is probed the same way, so a host's connection that can't keep a profile's
   guarantee fails the start.

## Options considered

| Force | StackExchange.Redis 3.4.35 | Valkey.Glide 1.2.0 | A client of the product's own |
| --- | --- | --- | --- |
| F1 servers | Valkey, as NCode.Identity's adapter runs it; Garnet, tested by Garnet's team; Redis | Valkey 7.2–9.2 as its official client, Redis 6.2–7.2; Garnet not listed, and its Pub/Sub failed against Garnet | Whatever the conformance kit proves |
| F2 hosts | Managed, 5.7 MB, so every runtime identifier | Native Rust core, 43.0 MB; no musl for C#, so no Alpine | Managed |
| F3 license | MIT, transitive dependencies MIT | Apache-2.0, plus the native core's third-party licenses | None to check |
| F4 failure classification | Exception and command status, verified by tests | Not documented for C# | Exact, from its own socket writes |
| F5 commands | Scripts, arbitrary commands, cluster routing, Pub/Sub; no blocking commands | All, blocking commands included | All, once written |
| F6 host's connection | `IConnectionMultiplexer` from NCode.Identity and Aspire | A second connection beside the host's | A second connection beside the host's |
| F7 cost | A dependency to keep current | A dependency plus a native library per platform | Connection management, TLS, ACL, cluster topology, `MOVED` and `ASK` redirects, reconnection and resubscription, kept for three vendors |
| F8 one client | The client Microsoft's cache, output cache, data protection, SignalR and Aspire packages use | A second client beside theirs in any service that uses them | A second client beside theirs |

- **StackExchange.Redis.** Chosen: it is the only candidate that meets F1, F2, F6 and F8 together, so it can be the one
  client of every NCode product. What it lacks, blocking commands, the provider designs around (rule 5). Its exceptions
  are coarser than the product's own socket writes would be, so its classification is pinned by tests (rule 4).
- **Valkey.Glide.** Rejected, although it is Valkey's own client and the closest fit for Valkey alone. It can't be the
  one client (F8), because Microsoft's packages take StackExchange.Redis's types. Its Pub/Sub failed against Garnet,
  which carries notifications and watches (F1). Alpine hosts can't run its native core, and every consumer ships its
  43.0 MB of native binaries (F2). Its availability-zone affinity and cloud IAM authentication serve an application
  tuning reads and credentials on a managed service. A coordination library doesn't need them, and the blocking
  commands it adds rule 5 doesn't need either.
- **A client of the product's own.** Rejected: it would answer F4 exactly, but at the cost of reimplementing and
  maintaining the connection, cluster and reconnection behavior of a mature client for three vendors (F7). Nothing in
  the design needs the precision a mature client lacks.
- **NRedisStack and FreeRedis.** Not considered further. NRedisStack is StackExchange.Redis with commands for Redis
  modules the provider doesn't use. FreeRedis targets .NET Standard 2.0 and .NET Framework only.
- **No RESP provider.** Rejected: ADR-1007 makes it part of the first stable release.

## Consequences

- A host on Valkey or Garnet, including one already using StackExchange.Redis through NCode.Identity or Aspire, adds
  the provider without a new connection, and a Redis profile needs no other client.
- The notification and watch decisions are made over Pub/Sub and non-blocking reads, with no blocking command.
- A major version of StackExchange.Redis changes the seam and the configurator, and nothing else. Rule 3 keeps that
  true, and review holds the primitives to it.
- A wrong failure classification would mislead the history checker. The fault-injection tests of rule 4 are part of the
  provider's conformance run, not an extra.
- A shared multiplexer brings the host's settings, such as its backlog policy and protocol, into the provider. The
  probe checks what the profile's guarantees depend on, and the configurator's documentation names the settings a
  shared connection must keep.
- You chose wrong if a profile's guarantee fails in the conformance kit because of the client rather than the server,
  if the seam needs blocking commands to meet a contract, or if StackExchange.Redis stops working with Valkey. Then a
  record that supersedes this one weighs Valkey.Glide again, once it has a musl build, passes the conformance kit
  against Garnet, and Microsoft's packages accept it, or a client of the product's own.

## References

- [Design: Packages](../designs/coordination.md#packages)
- [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md),
  [ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md),
  [ADR-1009](1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md),
  [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
- [nuget.org](https://www.nuget.org/) registration, search and package APIs, read 2026-11-25: versions, publication
  dates, download counts, package sizes, licenses, target frameworks and dependencies of StackExchange.Redis, RESPite,
  System.IO.Hashing, Valkey.Glide, NRedisStack and FreeRedis, and the StackExchange.Redis dependency of
  Microsoft.Extensions.Caching.StackExchangeRedis, Microsoft.AspNetCore.OutputCaching.StackExchangeRedis,
  Microsoft.AspNetCore.DataProtection.StackExchangeRedis, Microsoft.AspNetCore.SignalR.StackExchangeRedis and
  Aspire.StackExchange.Redis
- [Valkey: Clients](https://valkey.io/clients/), read 2026-11-25: the C# clients the Valkey project lists
- [Memorystore for Valkey: client library code samples](https://docs.cloud.google.com/memorystore/docs/valkey/client-library-code-samples),
  in search results read 2026-11-25
- Valkey GLIDE C# [changelog](https://github.com/valkey-io/valkey-glide-csharp/blob/main/CHANGELOG.md),
  [known issues](https://glide.valkey.io/reference/known-issues/) and
  [musl issue #192](https://github.com/valkey-io/valkey-glide-csharp/issues/192), read 2026-11-25
- A test run on 2026-11-25 of Valkey.Glide 1.2.0 and StackExchange.Redis 3.4.35 through their public APIs against
  `valkey/valkey:9` and `ghcr.io/microsoft/garnet` 2.2.14 (started with `--lua`) containers
- StackExchange.Redis documentation, read 2026-11-25: [overview](https://seredis.dev/),
  [Pipelines and Multiplexers](https://seredis.dev/PipelinesMultiplexers),
  [Async Timeouts](https://seredis.dev/AsyncTimeouts), [Using RESP3](https://seredis.dev/Resp3),
  [Configuration](https://seredis.dev/Configuration)
- Valkey GLIDE documentation, read 2026-11-25: [overview](https://glide.valkey.io/overview/),
  [installation](https://glide.valkey.io/how-to/installation/),
  [Pub/Sub model](https://glide.valkey.io/concepts/client-features/pubsub-model/), and the
  [C# client's README](https://github.com/valkey-io/valkey-glide-csharp)
- [Garnet: API compatibility](https://microsoft.github.io/garnet/docs/welcome/compatibility) and
  [Welcome to Garnet](https://microsoft.github.io/garnet/docs), in search results read 2026-11-25: the clients
  Garnet is tested with
- [Microsoft Learn: Aspire Stack Exchange Redis integration](https://learn.microsoft.com/dotnet/aspire/caching/stackexchange-redis-integration),
  in search results read 2026-11-25: `AddRedisClient` registers an `IConnectionMultiplexer`
- NCode.Identity's `Directory.Packages.props`, a product of this organization: its Valkey adapter's client
