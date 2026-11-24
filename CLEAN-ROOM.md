# Clean-room policy

NCode.Coordination is an original work. Its designs come from ideas, specifications, papers and public documentation;
its code is written here, never derived from another project's code. This file says what a contributor, human or
agent, may consult, what they may not, and how the sources of each design are recorded.

## May consult

- **Specifications and standards**: IETF RFCs, protocol specifications published as documents (the RESP protocol
  description, the PostgreSQL protocol and SQL documentation, the etcd and Consul API references).
- **Papers and books**: academic papers and their published talks, such as those on consensus, leases, lock services,
  coordination services and linearizability checking.
- **Public product documentation**: user guides, design and architecture documents, operations guides, consistency
  statements and published test reports (Jepsen analyses, for example), whether on a website or as documentation files
  in a repository.
- **Packages as dependencies**: a published library, such as a protocol client, used through its public API and its
  documentation.

## May not consult

- **Another project's source code**, including its tests, scripts and build files, for any project in this product's
  area: Redis, Valkey, Garnet, Stalwart Keep, etcd, ZooKeeper, Consul, Nomad, Orleans, Akka, Apache Pekko,
  MassTransit, and the client libraries this product depends on. Browsing a repository to reach its documentation is
  fine; opening its source files is not.
- **Decompiled or disassembled code** of any of them.

## Taken, and not taken

- **Ideas, concepts and published guarantees are taken**: sessions, leases, fencing tokens, revisions, watches,
  membership. They are named in this product's own words.
- **Text is not taken**: no documentation prose, diagrams, tables, examples or test vectors are copied. A protocol's
  own names (a RESP command, an etcd API field) are used where interoperating with it requires them.

## Recording sources

Every decision record lists, under References, the sources its design drew on. Sources consulted outside a decision
are added to [`docs/provenance.md`](docs/provenance.md) with the date and what was read. An agent that works on this
product is given this policy, fetches no source file, and records what it read.

## Limits of this policy

- **A language model's training data is outside its control.** An agent that writes code here may have been trained
  on public source code, including projects named above. The policy keeps the designs traceable to the sources each
  record lists and keeps source code out of every session's input; it cannot certify what a model learned before.
  Review looks for code that resembles another project's beyond what the shared protocol or idea requires.
- **It addresses copying, not patents.** Whether a design is free to use is a separate question, decided with legal
  advice when it arises.
