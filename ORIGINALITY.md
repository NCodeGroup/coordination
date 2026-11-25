# Originality

NCode.Coordination is an original work. **No part of it is a derivative work of another project**: not its code, its
tests, its documentation or its interface definitions. This is a hard requirement. The method below, a clean room
backed by checks, is how the product meets it; a method that stops serving the requirement is changed, never the
requirement.

This file binds every contributor, human or agent, and every instruction an agent gives a subagent, a workflow or
another session.

## What counts as derivative

Copyright protects expression, not ideas. The product takes ideas freely and takes no one's expression:

- **Derivative**: copied or adapted code, including a port or translation into another language (rewriting another
  project's Rust or Java in C# is still its work); copied tests, test vectors, examples, documentation prose, diagrams
  or tables; an interface definition file copied from another project, such as its `.proto` or schema files; code
  reproduced from memory.
- **Not derivative**: concepts, algorithms and guarantees described in papers, specifications and documentation, such
  as sessions, leases, fencing tokens, revisions and watches, expressed in this product's own words and code; the names
  and formats a protocol requires for interoperating with it, such as a RESP command or an API field; this product's
  own code, and code its copyright holder, NCode Group, owns in its other repositories.

## The method

1. **Sources are public descriptions, never source code.** A contributor consults specifications, RFCs, papers, books,
   public product documentation and published test reports. A contributor does not open another project's source code
   for this product — its code, tests, scripts, build files or interface definitions — for any project in the product's
   area: Redis, Valkey, Garnet, Stalwart Keep, etcd, ZooKeeper, Consul, Nomad, Orleans, Akka, Apache Pekko,
   MassTransit, and the client libraries the product depends on. Browsing a repository to reach its documentation
   files is fine; opening its source files is not. Decompiled code counts as source.
2. **Ideas are restated, never transcribed.** A design is written from an understanding of the idea, in the product's
   own structure and words, not by following another product's documentation section by section.
3. **Interoperability is written from the reference.** Where a protocol fixes names, numbers or formats, they are
   taken from its published reference and written here, never copied from another project's files.
4. **Sources are recorded.** Every decision record lists its sources under References, and anything else consulted is
   added to [`docs/provenance.md`](docs/provenance.md) with the date and what was read.

## Dependencies

A dependency is used, not derived from:

- **Permissive licenses only**: MIT, Apache-2.0, the BSD licenses, the PostgreSQL License, or an equivalent;
  `build/allowed-licenses.json` lists their SPDX identifiers. No dependency under the GPL, LGPL, AGPL, SSPL, a
  source-available license or any license whose terms reach the code that uses it.
- **Through its public API and documentation.** Its source is not read, and none of its files are copied or vendored
  into this repository.
- **A server is not a dependency.** A provider talks to a server over its protocol; the server's license does not
  reach the provider's code.

## Checks

- **Review** looks for code, tests or prose that resemble another project's beyond what a shared protocol or idea
  requires, and for any file whose origin is not this repository. It is the only check against code a language model
  reproduces from what it learned.
- **License checking** in CI fails on a dependency, transitive ones included, whose license is not on the permitted
  list.

License checking runs in `.github/workflows/originality.yml` on every pull request
([ADR-1009](docs/decisions/1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md)), and
`./build/Test-Licenses.ps1` runs it from a checkout. No snippet scan runs: no service the project can use compares its
code with public open-source code, as ADR-1009 records.

## Limits

- **A language model's training data is outside the method's reach.** An agent that writes code here may have been
  trained on public source code, including projects named above, and could reproduce part of it. The method keeps
  source out of every session's input and traces each design to public sources, and review looks for reproduced
  code; neither certifies what a model learned, and no automated check compares the code with public source.
- **Patents are a separate question.** Originality addresses copyright. Whether a design is free to use is decided
  with legal advice when it arises.
