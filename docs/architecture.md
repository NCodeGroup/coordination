# Architecture

[//]: # "Maintainers: describe the components, as a diagram and a table (Component, Role), and the rules the design
keeps."

How NCode.Coordination is built. [assurance-case.md](assurance-case.md) explains why the design meets its security
requirements.

## Components

| Component | Role |
| --- | --- |
| `NCode.Coordination.Abstractions` | The contracts: an interface per primitive with its named operations, outcomes and failures, structured keys, the guarantee vocabulary, expiry and time windows, and the declarations and requirements the composition checks. |
| `NCode.Coordination` | The composition: providers registered on one builder with `AddCoordination`, bound by primitive and keyspace; the guarantee registry; every requirement checked before the host's hosted services start; and the primitive services that route each operation to its bound provider, tracing and measuring it. |

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
