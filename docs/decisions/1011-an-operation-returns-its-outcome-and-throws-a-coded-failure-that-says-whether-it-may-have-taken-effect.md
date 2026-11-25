# 1011. An operation returns its outcome, and throws a coded failure that says whether it may have taken effect

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

Operations have expected outcomes that callers branch on: a lost compare-and-swap, a lease another node holds, a
duplicate occurrence, an ended session. They also fail: the provider is unreachable or times out, the server refuses its
credentials, a server limit is exceeded, or the call has a shape the provider does not support.

etcd reports a failed comparison in the transaction's response, and Consul answers a lost check-and-set with `false`,
while a dropped connection leaves a write's fate unknown (read 2026-11-25). The history checker of
[ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md) must
know whether a failed write may have applied.

## Decision

**An operation returns every expected outcome in a result, and throws one coded exception for a failure, which says
whether the operation may have taken effect.**

1. **Outcomes are results.** Each operation returns a result whose status enum names every expected outcome. Status
   enums are appended to, never reordered. Absence on a read is `null`, never an exception.
2. **Failures throw `CoordinationException`.** Its `Code` comes from an open vocabulary, `CoordinationErrorCodes`
   (`unavailable`, `unsupported`, `limit-exceeded`, `access-denied`), which a provider extends with names under the
   vocabulary-name rule; code never matches message text. Its `OutcomeUnknown` is true when the request may have
   reached the server and applied, and false only when the provider knows it did not.
3. **Invalid arguments throw.** `ArgumentException` or `ArgumentNullException` is thrown before anything is sent.
4. **Cancellation throws `OperationCanceledException`.** A cancelled write's outcome is unknown. Waits and streams end
   only by cancellation or failure.
5. **Guards fail closed.** An occurrence, lease or claim never reports success when the provider cannot answer.
6. **`unsupported` is for a shape a primitive-level declaration cannot express.** Examples are a transaction across
   partitions on a provider that keeps atomicity within a partition only, and an options extension the provider does not
   honor. The composition refuses an unsupported primitive at startup
   ([ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)).

## Options considered

- **Exceptions for outcomes.** Rejected: a lost compare-and-swap is normal flow on a hot path.
- **Results for failures too.** Rejected: an unchecked failed guard passes.
- **An exception type per failure.** Rejected: it is a closed hierarchy, where a code is an open vocabulary.

## Consequences

- Callers switch on a status.
- The history checker records an operation with an unknown outcome as indeterminate.
- Providers map their server's errors to codes.

## References

- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
- etcd v3.5, "etcd API" (<https://etcd.io/docs/v3.5/learning/api/>)
- Consul HTTP API, [KV](https://developer.hashicorp.com/consul/api-docs/kv) and
  [transactions](https://developer.hashicorp.com/consul/api-docs/txn)
- `.github/instructions/csharp.instructions.md`
