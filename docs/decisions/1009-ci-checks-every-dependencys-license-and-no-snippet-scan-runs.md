# 1009. CI checks every dependency's license, and no snippet scan runs

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group
- **Amends:** 1001

## Context

[ADR-1001](1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md) backs
the clean room with two checks in CI: snippet scanning, which compares the repository's code with a database of public
open-source code, and license checking, which fails on a dependency outside the licenses
[ORIGINALITY.md](../../ORIGINALITY.md) permits. Snippet scanning is the only automated check aimed at code a
language model reproduces from what it learned.

The tools differ in what they offer an open-source project (read 2026-11-24 and 2026-11-25; sources in
[provenance](../provenance.md)):

- **SCANOSS** matches files and snippets against an open knowledge base that its free service answers without a key,
  under terms that promise no availability and let the service limit use. The quota is shared and anonymous: the first
  scan this repository ran was refused with "Rate limit exceeded" and a retry after about six days. SCANOSS offers no
  API key this project can use (found 2026-11-25).
- **FOSSA and Black Duck** match snippets in paid tiers only.
- **ScanCode Toolkit** finds licenses and copyrights in the repository's own files; it does not match code against a
  corpus.
- **nuget-license** (Apache-2.0), a .NET tool, lists every NuGet package of a solution, transitive ones included, from
  the restored assets, and exits non-zero when one's license is not on an allow list of SPDX identifiers.
- **GitHub's dependency review** reads NuGet project files but not their static transitive dependencies, so it would
  miss most of the packages that reach a build.

## Decision

**`.github/workflows/originality.yml` checks every NuGet dependency's license, transitive ones included, against the
licenses ORIGINALITY.md permits, on every pull request, on every push to the default branch and in the merge queue. No
snippet scan runs: no service the project can use compares its code with public open-source code, and review is the
check against reproduced code.**

1. **Licenses.** `nuget-license` is a local tool in `.config/dotnet-tools.json`. `build/Test-Licenses.ps1` restores
   the solution and runs it over every package, transitive and development-only ones included, against
   `build/allowed-licenses.json`: the SPDX identifiers of the licenses ORIGINALITY.md permits. A license is added to the
   list only by a change to ORIGINALITY.md's permitted licenses; a package whose license the tool cannot read fails
   the check too, and its dependency is replaced or an override mechanism is decided in a record of its own.
2. **Locally too.** `build/Test-Licenses.ps1` runs from a contributor's checkout, and `AGENTS.md` names it for every
   change that adds or updates a package.
3. **No snippet scan.** Review looks for code, tests or prose that resemble another project's beyond what a shared
   protocol or idea requires, and ORIGINALITY.md states that no automated check compares the code with public source.
   A snippet scanner joins the checks through a record that supersedes this one, once a service is available that the
   project can use without a paid license and without a shared quota that fails its pull requests.
4. **ADR-1001 amended.** Its item 6 and its consequence about the snippet scanner's matches are replaced by this
   record; the rest of ADR-1001 holds.

## Options considered

- **SCANOSS's action against its free knowledge base.** Rejected: the shared anonymous quota refused the first scan
  for about six days, and the project has no API key to lift it, so the check fails pull requests without scanning.
- **The same scan, advisory.** Rejected: a check that cannot run gives no assurance, and a red mark on every pull
  request teaches reviewers to ignore CI.
- **FOSSA or Black Duck.** Rejected: snippet matching is in their paid tiers only.
- **ScanCode Toolkit for snippets.** Rejected: it does not match against a corpus of public code.
- **GitHub's dependency review for licenses.** Rejected: it misses NuGet's transitive dependencies.

## Consequences

- Code a model reproduces from its training is found only by review, a limit ORIGINALITY.md states.
- A dependency whose license is outside the list fails CI before review.
- Adding a snippet scanner later is a superseding record, a workflow job and a line in ORIGINALITY.md's Checks.

## References

- [`ORIGINALITY.md`](../../ORIGINALITY.md)
- [ADR-1001](1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md)
- [Provenance](../provenance.md): SCANOSS, nuget-license, GitHub dependency graph, FOSSA and ScanCode documentation
