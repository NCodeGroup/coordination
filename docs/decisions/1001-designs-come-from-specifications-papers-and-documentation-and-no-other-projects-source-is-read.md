# 1001. Designs come from specifications, papers and documentation, and no other project's source is read

- **Status:** Proposed
- **Date:** 2026-11-24
- **Deciders:** NCode Group

## Context

The products that solve this problem are open source under licenses that range from permissive to network copyleft.
Code derived from one of them carries its license, and a design learned by reading its code is hard to tell apart from
code derived from it. The ideas themselves are published in papers, specifications and documentation, and are free to
use.

## Decision

**NCode.Coordination is a clean-room work: its designs come from specifications, papers and public documentation,
recorded per decision, and no contributor reads another project's source code for it.**

1. [`CLEAN-ROOM.md`](../../CLEAN-ROOM.md) lists what may and may not be consulted, and binds every contributor and
   every agent, including the instructions an agent gives another.
2. Each decision record lists its sources under References; anything else consulted goes into
   [`docs/provenance.md`](../provenance.md).
3. A published package is used as a dependency through its public API; its source is not read.
4. Ideas are taken in the product's own words; documentation text, diagrams, examples and test vectors are not copied.

## Options considered

- **No policy.** Rejected: a later question about origin would have no record to answer it.
- **Port or fork an existing implementation.** Rejected: the result is a derivative work under that project's license.

## Consequences

- Every design is traceable to public sources.
- Some designs take longer, because a documented guarantee must be turned into an implementation without seeing how
  others did it.
- An agent's training data is outside the policy's reach, so review watches for resemblance beyond what a shared
  protocol requires, and the policy says so.

## References

- [`CLEAN-ROOM.md`](../../CLEAN-ROOM.md)
