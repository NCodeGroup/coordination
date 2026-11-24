# 1001. The product is an original work, and a clean-room method keeps it free of derivative work

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

The products that solve this problem are open source under licenses that range from permissive to network copyleft
and source-available. Work derived from one of them carries its license and its owner's claims, and the requirement is
that no part of NCode.Coordination is derived from another project. Ideas, algorithms and published guarantees are
free to use; code, tests, documentation text and interface files are another's expression, including when they are
translated into another language. A design learned by reading another project's code is hard to tell apart from code
derived from it, and an agent trained on public code can reproduce some of it unprompted.

## Decision

**NCode.Coordination is an original work, and no part of it is a derivative work of another project. A clean-room
method, permissive dependencies used only through their public API, recorded sources, and review, snippet and license
checks keep it so.**

1. [`ORIGINALITY.md`](../../ORIGINALITY.md) defines what counts as derivative, the method, the dependency rules and
   the checks, and binds every contributor and agent, including the instructions an agent gives another.
2. Designs come from specifications, papers and public documentation; no contributor reads another project's source
   code for this product, and a port or translation of another project's code is derivative.
3. Protocol names and formats are written from the protocol's published reference; no interface file is copied.
4. Dependencies are permissively licensed, used through their public API, and never copied or vendored.
5. Each decision record lists its sources; anything else consulted goes into [`docs/provenance.md`](../provenance.md).
6. CI scans the code for snippets that match public open-source code and checks dependency licenses.

## Options considered

- **A clean room alone.** Rejected as insufficient: it controls what contributors read, not what an agent reproduces
  from training, so snippet scanning is added.
- **Separate teams, one writing specifications from others' products and one implementing them.** Not required here:
  the specifications are written from public documentation, not from source, so no one has seen the code the
  implementers must not; it stays the method if a contributor ever has.
- **Port or fork an existing implementation.** Rejected: the result is a derivative work under that project's license.
- **No policy.** Rejected: a later question about origin would have no record to answer it.

## Consequences

- Every design is traceable to public sources, and every dependency to a permitted license.
- Some designs take longer, because a documented guarantee is turned into an implementation without seeing another.
- A provider for a protocol whose only published client is under a license outside the permitted set writes its own
  client from the protocol's reference.
- The snippet scanner can report matches the protocol explains; each is reviewed and recorded, never silenced in bulk.

## References

- [`ORIGINALITY.md`](../../ORIGINALITY.md)
- [Provenance](../provenance.md)
