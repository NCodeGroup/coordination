# Architecture

[//]: # "Maintainers: describe the components, as a diagram and a table (Component, Role), and the rules the design
keeps."

How NCode.Coordination is built. [assurance-case.md](assurance-case.md) explains why the design meets its security
requirements.

## Components

| Component | Role |
| --- | --- |
| `NCode.Coordination.Abstractions` | The contracts: an interface per primitive with its named operations, outcomes and failures, structured keys, the guarantee vocabulary, expiry, time windows and the holder's local deadline, and the declarations and requirements the composition checks. |
| `NCode.Coordination` | The composition: providers registered on one builder with `AddCoordination`, bound by primitive and keyspace; the guarantee registry; every requirement checked before the host's hosted services start; and the primitive services that route each operation to its bound provider, tracing and measuring it; and the in-process provider, added with `AddInProcessProvider`, which keeps every primitive in the process's memory, with sessions that end what is bound to them, eager expiry, and watches that replay the last 1,024 changes of a keyspace; and the recipes, each built only on the primitives' contracts and required with `RequireRecipe`: locks, read-write locks, barriers, singletons, partition ownership, single-flight, fixed-window, sliding-window and token-bucket rate limiters that are `System.Threading.RateLimiting` limiters, and cache invalidation ([ADR-1020](decisions/1020-recipes-use-only-the-primitives-contracts-declare-the-guarantees-they-rely-on-and-wait-on-releases-and-watches.md)). |
| `NCode.Coordination.Hosting` | The host's session: `AddCoordinationHosting` binds the node's options strictly, from configuration or code; once the composition's requirements pass, the session opens, joins the cluster's membership with the node's id, address and metadata, is renewed on the host's clock and replaced when lost, and closes once every hosted service has stopped, releasing what is bound to it; features take it from `IHostSessionService`, `RequireHostSession` fails the start when no binding keeps it, and `AddCoordinationCheck` reports the host not ready without it ([ADR-1021](decisions/1021-a-host-holds-one-session-for-its-lifetime-joins-its-membership-with-it-and-closes-it-as-it-stops.md)). |
| `NCode.Coordination.Testing` | The conformance kit: a contract suite per primitive, xUnit classes a provider's test project derives from with the provider under test and a way to advance its clock, a way to drop its connections, and concurrent runs whose recorded histories a checker verifies for linearizability and declared delivery ([ADR-1017](decisions/1017-the-conformance-kit-is-a-set-of-xunit-contract-classes-a-providers-tests-derive-from.md), [ADR-1019](decisions/1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md)). |

[The design](designs/coordination.md#packages) names the other packages and the phase that builds each.

## Design Rules

- Decisions the design rests on are recorded in [decisions/](decisions/README.md).
- Warnings are errors ([ADR-0100](decisions/project-template/0100-warnings-are-errors-in-every-project.md)), and
  disallowed APIs are banned at build time
  ([ADR-0102](decisions/project-template/0102-disallowed-apis-are-banned-at-build-time.md)).
- The public API is tracked and evolves compatibly
  ([ADR-0131](decisions/project-template/0131-public-api-is-tracked-and-evolves-compatibly.md)), and each package is
  validated against the last published package
  ([ADR-0147](decisions/project-template/0147-packages-are-validated-against-the-last-published-package.md)).

## Build and Release

CI runs the project's commands on every pull request
(`.github/workflows/ci.yml`). `.github/workflows/originality.yml` also checks every pull request's NuGet dependencies,
transitive ones included, against the permitted licenses
([ADR-1009](decisions/1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md)).
Releases are tagged and published as
[ADR-0016](decisions/project-template/0016-releases-are-tagged-and-published-when-their-pull-request-merges.md)
records. `publish.yml` tests and packs the tagged commit, pushes to nuget.org through trusted publishing once a
maintainer approves the `nuget` environment, and calls `release-provenance.yml` and `raise-baseline.yml`
([ADR-0137](decisions/project-template/0137-the-release-workflow-starts-package-publishing.md),
[ADR-0151](decisions/project-template/0151-release-follow-ups-are-jobs-publish-calls.md)).
