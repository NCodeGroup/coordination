# Architecture Decision Records

How NCode.Coordination records its decisions: one short Markdown file per decision, kept in this folder and changed in
the same pull request as the code it explains.

## When to Write One

Write a record for a non-obvious choice that someone would otherwise question again: a design with real alternatives,
a trade-off that looks wrong without its reasons, or a departure from the project template. The conventions themselves
go in `AGENTS.md` or the instruction files it lists; a record explains why a convention is what it is.
`/analyze-decision` compares the options and writes one, in a worktree of its own, where it lands with the plan that
builds it. Work bigger than one plan starts as a design document in `docs/designs/`, from
[its template](../designs/_template.md), and each of its decisions becomes a record here when its phase lands.

## How to Write One

- Copy [0000-template.md](0000-template.md) to `NNNN-kebab-case-title.md`, starting from `1000`. Numbers below 1000
  are the project template's, in [project-template/](project-template/README.md), so an ADR number names one record
  across both folders. `.template/Test-AdrIndex.ps1` fails a new record numbered below 1000; records the project
  already had keep their numbers.
- Take a number no one else holds. Fetch first, then take the next number above both the highest record numbered 1000
  or more on `origin/<default branch>` and any record an open branch, pull request or worktree is writing. Check again
  after rebasing or merging the default branch, and when the number was taken meanwhile, renumber the file, its title,
  its index row and every link to it. Two records never share a number; `.template/Test-AdrIndex.ps1` fails when they
  do.
- Add the record to the index below, in the same change.

## Statuses

- `Proposed`: the decision is open.
- `Accepted`: the decision holds.
- `Deprecated`: the decision no longer applies, and nothing replaces it.
- `Superseded by NNNN`: a later record replaces it whole.

Never rewrite the decision of an accepted record; fixing a typo or a broken link is fine. A changed decision is a new
record, and the old record's status and index row say so. When the new record changes only part of the old one, the
old one stays `Accepted` with a note, "Accepted (amended by NNNN)", and the new one's index row says "(amends NNNN)".
The old record's text is not edited beyond its status line.

## What a Record Is

A record is a decision the project made, not an authority. Where an external standard or current security practice
governs, it wins: a record that conflicts with one is superseded to align with it, never cited to justify the
deviation.

## Writing Rules

- Write every section in the present tense, so it reads the same a year later: what is, and why. Avoid words that
  date it, such as "now", "new", "currently", "previously", "originally", "we decided to change" or "the first
  version"; the history belongs in the commit log and in the records a record supersedes.
- Give a volatile fact (a price, a quota, a limit, a supported version) its source and the date it was read, and mark
  an estimate as an estimate.
- Keep a record short, and link to the code, issues and records it depends on rather than restating them.

## The Template's Decisions

The project template's own decisions are in [project-template/](project-template/README.md). Template updates replace
that folder. To decide otherwise, write a record here that says "Supersedes: project-template ADR-NNNN" and list the
override in `AGENTS.md`.

## Index

One row per record of NCode.Coordination's own, numbered from 1000; an empty table means it has none. The template's
records are indexed in [project-template/](project-template/README.md).

| ADR | Decision | Status |
| --- | --- | --- |
| [1000](1000-ncode-coordination-is-a-general-coordination-library-for-the-nodes-of-a-cluster.md) | NCode.Coordination is a general coordination library for the nodes of a cluster | Proposed |
| [1001](1001-the-product-is-an-original-work-and-a-clean-room-method-keeps-it-free-of-derivative-work.md) | The product is an original work, and a clean-room method keeps it free of derivative work | Proposed (amended by 1009) |
| [1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md) | Primitives declare guarantees, and the composition refuses a requirement no provider meets | Proposed |
| [1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md) | Providers are named for the protocol they speak and carry a profile per vendor | Proposed |
| [1004](1004-a-node-holds-a-session-that-binds-its-leases-and-membership-and-every-lease-grant-carries-a-fencing-token.md) | A node holds a session that binds its leases and membership, and every lease grant carries a fencing token | Proposed |
| [1005](1005-notifications-carry-a-sequence-and-tell-a-subscriber-to-resync-and-the-product-brokers-no-messages.md) | Notifications carry a sequence and tell a subscriber to resync, and the product brokers no messages | Proposed |
| [1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md) | Every provider passes one conformance kit that checks recorded histories against each guarantee | Proposed |
| [1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md) | The first stable release carries the whole model, proven on three providers and by its first consumer | Proposed |
| [1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md) | Expiry is a duration on the provider's declared clock, and an instant becomes one at the caller with an allowance for skew | Proposed |
| [1009](1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md) | CI checks every dependency's license, and no snippet scan runs (amends 1001) | Proposed |
| [1010](1010-vocabulary-values-are-sealed-record-classes-so-no-default-instance-skips-validation.md) | Vocabulary values are sealed record classes, so no default instance skips validation | Proposed |
| [1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md) | An operation returns its outcome, and throws a coded failure that says whether it may have taken effect | Proposed |
| [1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md) | Each primitive is an interface whose operations are named with the guarantee dimensions they carry | Proposed |
| [1013](1013-ownership-is-granted-with-a-fencing-token-and-an-owner-token-and-bound-to-a-session-or-a-time-to-live.md) | Ownership is granted with a fencing token and an owner token, and bound to a session or a time to live | Proposed |
| [1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md) | Records carry a keyspace-wide revision, and watches and transactions are written in it | Proposed |
| [1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md) | Occurrences fail closed, windows align to the epoch, and notifications number each topic's messages | Proposed |
| [1016](1016-a-host-binds-providers-by-primitive-and-keyspace-and-its-start-fails-on-every-requirement-a-binding-cannot-keep.md) | A host binds providers by primitive and keyspace, and its start fails on every requirement a binding cannot keep | Proposed (amended by 1020) |
| [1017](1017-the-conformance-kit-is-a-set-of-xunit-contract-classes-a-providers-tests-derive-from.md) | The conformance kit is a set of xUnit contract classes a provider's tests derive from | Proposed |
| [1018](1018-a-holders-local-deadline-starts-before-its-request-and-ends-an-allowance-for-drift-before-the-time-to-live.md) | A holder's local deadline starts before its request and ends an allowance for drift before the time to live | Proposed |
| [1019](1019-concurrent-runs-record-histories-and-a-checker-searches-each-keys-history-for-a-linearization.md) | Concurrent runs record histories, and a checker searches each key's history for a linearization | Proposed |
| [1020](1020-recipes-use-only-the-primitives-contracts-declare-the-guarantees-they-rely-on-and-wait-on-releases-and-watches.md) | Recipes use only the primitives' contracts, declare the guarantees they rely on, and wait on releases and watches (amends 1016) | Proposed |
| [1021](1021-a-host-holds-one-session-for-its-lifetime-joins-its-membership-with-it-and-closes-it-as-it-stops.md) | A host holds one session for its lifetime, joins its membership with it, and closes it as it stops | Proposed |
| [1022](1022-the-resp-provider-speaks-through-stackexchange-redis-and-can-share-the-hosts-connection.md) | The RESP provider speaks through StackExchange.Redis, and can share the host's connection | Proposed |
| [1023](1023-the-resp-provider-runs-each-operation-as-one-command-or-one-lua-script-and-never-as-a-client-side-transaction.md) | The RESP provider runs each operation as one command or one Lua script, and never as a client-side transaction | Proposed (amended by 1025) |
| [1024](1024-the-resp-provider-keeps-each-keyspace-in-one-cluster-slot-with-its-revision-beside-it.md) | The RESP provider keeps each keyspace in one cluster slot, with its revision beside it | Proposed |
| [1025](1025-resp-notifications-and-watches-are-numbered-in-the-writing-script-published-over-pubsub-and-resync-on-any-doubt.md) | RESP notifications and watches are numbered in the writing script, published over Pub/Sub, and resync on any doubt (amends 1023) | Proposed |
| [1026](1026-resp-counters-keep-counts-up-to-the-largest-integer-a-lua-number-holds-exactly.md) | RESP counters keep counts up to the largest integer a Lua number holds exactly | Proposed |
