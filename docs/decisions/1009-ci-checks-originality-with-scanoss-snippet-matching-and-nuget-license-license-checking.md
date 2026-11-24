# 1009. CI checks originality with SCANOSS snippet matching and nuget-license license checking

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

[ORIGINALITY.md](../../ORIGINALITY.md) requires two checks in CI from the first package on: snippet scanning that
compares the repository's code with a database of public open-source code and fails on a match a shared protocol does
not explain, and license checking that fails on a dependency outside the permitted licenses
([ADR-1001](1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md)). A match
that review finds explained is recorded one by one, never silenced in bulk.

The tools differ in what they do (read 2026-11-24; sources in [provenance](../provenance.md)):

- **SCANOSS** matches files and snippets against its open knowledge base of public code, which its service answers free
  and without a key. Its GitHub Action, `scanoss/gha-code-scan` (MIT), fails the job when a policy fails or the scan
  errs; its "undeclared" policy fails on any matched component the repository's `scanoss.json` does not declare, and a
  declaration can name the file it covers. The knowledge base's terms promise no availability and allow the service to
  limit use.
- **FOSSA and Black Duck** match snippets in paid tiers only.
- **ScanCode Toolkit** finds licenses and copyrights in the repository's own files; it does not match code against a
  corpus.
- **nuget-license** (Apache-2.0), a .NET tool, lists every NuGet package of a solution, transitive ones included, from
  the restored assets, and exits non-zero when one's license is not on an allow list of SPDX identifiers.
- **GitHub's dependency review** reads NuGet project files but not their static transitive dependencies, so it would
  miss most of the packages that reach a build.

## Decision

**A workflow of its own, `.github/workflows/originality.yml`, runs both checks on every pull request, on every push to
the default branch and in the merge queue: SCANOSS's action with the undeclared-component policy, and nuget-license
against an allow list of the licenses ORIGINALITY.md permits.**

1. **Snippets.** The action runs pinned to a commit, with its policy failures and scan errors failing the job, so an
   unreachable knowledge base fails closed. Its job holds only the permissions it needs to read the code and to
   annotate the pull request.
2. **Reviewed matches.** `scanoss.json` at the repository root holds one `settings.bom.include` entry per reviewed
   match, each naming the file's `path`, the matched component's `purl` and a `comment` that says why the match is not
   derived work (a shared protocol, NCode Group's own code, a license text). It holds nothing else: no entry without a
   path, no removal, replacement or skip, which would silence matches in bulk. `build/Test-SnippetReviews.ps1` checks
   that, and runs in CI before the scan.
3. **Licenses.** `nuget-license` is a local tool in `.config/dotnet-tools.json`. `build/Test-Licenses.ps1` restores
   the solution and runs it over every package, transitive and development-only ones included, against
   `build/allowed-licenses.json`: the SPDX identifiers of the licenses ORIGINALITY.md permits. A license is added to the
   list only by a change to ORIGINALITY.md's permitted licenses; a package whose license the tool cannot read fails
   the check too, and its dependency is replaced or an override mechanism is decided in a record of its own.
4. **Locally too.** Both scripts run from a contributor's checkout; the snippet scan runs in CI only.

## Options considered

- **A script of our own around SCANOSS's command-line client.** Rejected: it consults the same knowledge base, and
  would reimplement the policy and the pull-request annotations the action already provides.
- **FOSSA or Black Duck.** Rejected: snippet matching is in their paid tiers only.
- **ScanCode Toolkit for snippets.** Rejected: it does not match against a corpus of public code.
- **GitHub's dependency review for licenses.** Rejected: it misses NuGet's transitive dependencies.

## Consequences

- A pull request shows each match as an annotation, and its author either removes the code or adds a reviewed entry
  with its reason.
- Files that NCode Group publishes elsewhere, such as those the project template generates, and license texts can
  match; each is reviewed and recorded like any other match.
- When SCANOSS's knowledge base is unreachable or limits the project's use, the check fails and the pull request waits;
  an API key for SCANOSS's paid service is the remedy if that recurs.
- A new dependency whose license is outside the list fails CI before review.

## References

- [ORIGINALITY.md](../../ORIGINALITY.md)
- [ADR-1001](1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md)
- [Provenance](../provenance.md): SCANOSS, nuget-license, GitHub dependency graph, FOSSA and ScanCode documentation
