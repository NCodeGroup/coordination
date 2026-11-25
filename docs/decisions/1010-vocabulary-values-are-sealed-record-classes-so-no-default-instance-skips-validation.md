# 1010. Vocabulary values are sealed record classes, so no default instance skips validation

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

The contracts' values each carry an invariant that their constructor checks: keys and segments, keyspaces, guarantee,
primitive and operation names, time to live, time windows, revisions, fencing tokens, owner tokens and session ids.

A struct has a default instance that runs no constructor: `default(T)`, the elements of a new array and an unset field
all hold one. A default key would hold a default `ImmutableArray`, and a default fencing token would be 0, below every
real grant, so a resource that compares tokens would accept a write it must refuse.

The template's C# conventions prefer a `readonly record struct` for a small immutable value.

## Decision

**Every value is a `sealed record` class with a validating constructor or factory, so no instance exists that its
checks did not accept.**

1. **Records, not positional.** A value declares its constructor and get-only properties, with no positional parameters
   and no public setter.
2. **Equality by value.** A value that holds an `ImmutableArray` declares its `Equals` and `GetHashCode` explicitly.
3. **`ToString` is the text form.** It is the form a person reads or a provider encodes.
4. **Ordered values compare.** A value that orders, such as `Revision` and `FencingToken`, implements `IComparable<T>`
   and the comparison operators.
5. **Aggregates are classes.** Definitions, declarations, grants, results, views and transactions, which callers do not
   compare, are sealed classes with reference equality.
6. **Kept bytes are a copy.** A type that keeps a caller's bytes, such as a record's value or a notification's payload,
   keeps its own copy, made when it checks the length, and an operation that takes bytes reads them before its task
   completes, so a caller may reuse or return a pooled buffer once the call returns.

## Options considered

- **`readonly record struct`.** Rejected: the default instance bypasses validation, and every consumer would have to
  check for it.
- **Classes with reference equality.** Rejected: keys and names are compared and used as dictionary keys.

## Consequences

- Null is the only invalid value, and nullable annotations track it.
- Each value costs one allocation, small beside an operation's round trip.
- A value of up to the value limit is copied once when a type keeps it.
- This record overrides the C# conventions' preference for a `readonly record struct` for these types.

## References

- [Coordination design](../designs/coordination.md)
- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- [C# conventions](../../.github/instructions/csharp.instructions.md)
