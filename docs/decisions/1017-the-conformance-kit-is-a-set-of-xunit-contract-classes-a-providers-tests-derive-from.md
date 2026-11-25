# 1017. The conformance kit is a set of xUnit contract classes a provider's tests derive from

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

Every provider passes one conformance kit that starts with contract tests per primitive
([ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)).
Those tests must run once per provider without being rewritten for it, so a provider's test project supplies only the
provider and the tests stay in one place.

A server's clock cannot always be moved. A test that needs time to pass has to work against a provider whose clock is
faked and against one that is reached only by waiting. Providers also differ in the primitives and the guarantees they
declare
([ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)), so a
test cannot assume every provider serves every primitive or keeps every guarantee. The kit is a package a provider's
test project references, so it depends only on the abstractions
([project-template ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)).

## Decision

**The conformance kit is the package `NCode.Coordination.Testing`: one abstract xUnit contract class per primitive,
which a provider's test project derives from by supplying a provider and, when it can, a way to advance the provider's
clock.**

1. **References.** `NCode.Coordination.Testing` references `NCode.Coordination.Abstractions` and xUnit.net v3's
   `xunit.v3.extensibility.core` and `xunit.v3.assert`, and never `NCode.Coordination`
   ([project-template ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)).
2. **Contract classes.** There is one abstract class per primitive: `SessionContractTests`, `MembershipContractTests`,
   `LeaseContractTests`, `SemaphoreContractTests`, `ElectionContractTests`, `ClaimContractTests`, `RecordContractTests`,
   `RecordWatchContractTests`, `TransactionContractTests`, `OccurrenceContractTests`, `SetContractTests`,
   `CounterContractTests`, `SequenceContractTests` and `NotificationContractTests`. Each derives from
   `ProviderContractTests`. A provider's test project derives a sealed class from each and overrides
   `CreateTargetAsync`, which returns a `ConformanceTarget`: a provider created for that test and, optionally, a
   delegate that advances the provider's clock, by moving a fake clock or by waiting.
3. **Keyspace per test.** Each test works in a keyspace of its own: the cluster `conformance` and a segment made from a
   generated UUID. Tests that share a server never meet.
4. **Skips and failures.** A test of a primitive that the provider declares unsupported, or does not list, is skipped
   with the reason. A test that needs time to pass is skipped when the target cannot advance its clock. A test whose
   expected outcome depends on a declared guarantee, such as atomicity across partitions, checks the outcome that the
   declaration promises. A primitive that the provider declares supported but serves no service for fails the test.
5. **Expiry tests.** An expiry test uses a time to live of 2 seconds, checks the state is alive 1 second in and gone 2
   seconds after that, so a server that keeps whole seconds passes.
6. **Names.** The kit's tests are named `Method_State_Expected`, as the project's tests are, so the kit's folder turns
   off CA1707 and CA1861 in `.editorconfig`.
7. **First consumer.** The in-process provider in `NCode.Coordination` runs every suite in `tests/`.
8. **Streams.** A test reads a watch or a subscription only after its first item, which the provider yields once the
   watch is registered, and waits at most 10 seconds for each item; a test of a state watch waits for the view it
   expects, since views may be skipped.

## Options considered

- **A framework-neutral kit of plain methods.** Rejected: every provider writes a wrapper per test, and the wrappers
  drift from the kit.
- **A kit that composes through `AddCoordination`.** Rejected: it tests the routing, not the provider, and it needs a
  reference to `NCode.Coordination`.
- **One keyspace that is cleaned between tests.** Rejected: cleaning is a primitive under test, and a failure in it
  would fail every later test.

## Consequences

- A provider's test project references a compatible xUnit.net v3.
- The history checker and the fault injection of ADR-1006 join the same package.
- A consumer runs the suites against its own server configuration by deriving them.
- A provider's support for a primitive is proven by the same tests that skip a primitive it does not declare.

## References

- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
- [ADR-1008](1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)
- [ADR-1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)
- [ADR-1014](1014-records-carry-a-keyspace-wide-revision-and-watches-and-transactions-are-written-in-it.md)
- [ADR-1015](1015-occurrences-fail-closed-windows-align-to-the-epoch-and-notifications-number-each-topics-messages.md)
- [ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)
- xUnit.net, "What NuGet packages should I use? (v3)" (<https://xunit.net/docs/nuget-packages-v3>), read 2026-11-25
- xUnit.net, "What's new in v3" (<https://xunit.net/docs/getting-started/v3/whats-new>), read 2026-11-25
