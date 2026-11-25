# 1016. A host binds providers by primitive and keyspace, and its start fails on every requirement a binding cannot keep

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

The composition refuses at startup a requirement no provider meets
([ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)), and
each provider has a configurator of its own
([ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)). A provider
package references only the abstractions
([ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)), and the base abstractions
stay on the BCL alone
([ADR-0133](project-template/0133-abstractions-are-framework-free-and-tiered.md)).

Keyspaces carry tenant segments, so a span or a metric that recorded one would leak a tenant's identifier. Metrics have
one owner per signal and bounded dimensions
([ADR-0113](project-template/0113-metrics-have-one-owner-per-signal-and-bounded-dimensions.md)), and one activity
source is named like its meter
([ADR-0114](project-template/0114-one-activity-source-named-like-its-meter.md)).

## Decision

**A host registers providers on one builder and binds each to primitives or to keyspaces; when the host starts, the
composition validates the guarantees, declarations and bindings as one set and fails the start with every requirement a
binding cannot keep; every operation goes to the binding that serves its key and is traced and measured without its
keys.**

1. **Framework-free provider contracts.** `ICoordinationBuilder` and `ICoordinationProvider` are in the abstractions and
   use only BCL types. A provider is added as a `Func<IServiceProvider, ICoordinationProvider>` and resolves what it
   needs from that `IServiceProvider`. A provider's configurator is an extension method on `ICoordinationBuilder` in
   the provider's own package. A host calls `AddCoordination` on `IServiceCollection`, from the `NCode.Coordination`
   package, to get the builder.
2. **Binding.** A `ProviderBinding` names a provider, optionally lists primitives, and optionally has a keyspace. With
   no primitives listed it covers every primitive the provider's declaration supports. Its keyspace covers itself and
   every keyspace in the same cluster whose segments start with its segments. A key resolves to the binding of its
   primitive with the longest covering keyspace, and otherwise to the binding without a keyspace. Two bindings of one
   primitive with an equal keyspace, or both without one, overlap, and a requirement no binding serves is a gap. Both
   fail the start. Nested keyspaces are allowed, and the most specific wins.
3. **Requirement keyspace.** A requirement without a keyspace is checked against the binding without a keyspace only,
   the composition's default binding. A requirement with a keyspace is checked against the binding that serves it.
4. **Duplicate and undefined guarantees.** Two definitions of one guarantee name that agree in dimension, description
   and implications count as one, so two instances of one provider can be registered. A name defined twice with
   different meanings fails the start. A guarantee that a declaration, an implication or a requirement names and the
   registry does not define fails the start with its name.
5. **Single node.** `UseSingleNode()` declares every caller to be in this process, so a binding that keeps
   `consistency.linearizable-within-process` meets a requirement of `consistency.linearizable-until-failover` or
   `consistency.linearizable-across-failover`. No other guarantee is relaxed.
6. **Startup failures.** The composition collects every failure, in registration order, into one
   `CoordinationCompositionException` whose `Failures` lists them. It is thrown from a hosted lifecycle service's
   starting step, which runs before any hosted service starts. Outside a host, it is thrown the first time a primitive
   service resolves a binding. A provider factory's exception stops the composition and is rethrown as it was thrown,
   and the providers created before it are disposed with the container.
7. **Startup probes.** Once the composition is valid, the startup check calls each provider's `ProbeAsync` in
   registration order, before any hosted service starts, so a provider verifies that its server keeps what it declares,
   as ADR-1003 rule 2 requires. A composition that is invalid fails without probing any server. A probe's exception
   stops the start as it is. Outside a host nothing probes the providers.
8. **Primitive services.** Each built-in primitive is an interface
   ([ADR-1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)),
   and each has one canonical implementation registered in DI, named `Default<Name>`
   ([ADR-0105](project-template/0105-the-canonical-implementation-is-named-defaultfoo.md)). It resolves the binding for
   the key's keyspace and calls the provider's implementation. A provider exposes its implementations only through
   `ICoordinationProvider.GetPrimitiveService`, never through DI. When no binding covers a key, the call throws a
   `CoordinationException` coded `unsupported`, whose message names the primitive and the cluster and never the
   keyspace's segments.
9. **Provider lifetime.** The composition owns the providers it creates and disposes each one when the host stops,
   asynchronously when it can. It disposes every provider it created, also when the composition failed after creating
   them. A provider whose disposal throws doesn't stop the others from being disposed; the exception is rethrown once
   every provider is disposed, as an `AggregateException` when several throw. A failed composition stays failed for
   the life of the container.
10. **Telemetry.** The meter and the activity source are both named `NCode.Coordination`. One histogram,
    `ncode.coordination.operation.duration`, in seconds, records each operation, and one client span,
    `ncode.coordination.operation`, traces it. Their attributes are the provider, the primitive, the operation and the
    outcome (`success`, `failure` or `canceled`), and on a failure `error.type`, which is the exception's code or its
    type's full name. The outcome is `canceled` when the caller's token canceled the operation, and it sets no
    `error.type` and no error status; any other exception, a provider's own timeout included, is a `failure`. A failure
    sets the span's status to error with no description. No exception message, key, segment, value or payload is recorded. A stream is measured from its
    first read to its end, and a consumer that stops early counts as a success.
11. **Dependencies.** `NCode.Coordination` depends on the dependency injection and hosting abstractions packages only.
    It does not register `IMeterFactory`, whose default implementation is not in an abstractions package and which
    every generic host registers; a bare service collection calls `AddMetrics()`. This is a deliberate exception to a
    feature registering its own framework dependencies, taken to keep the abstractions-only rule.

## Options considered

- **A builder that exposes `IServiceCollection` from the abstractions.** Rejected: it would add a dependency injection
  package to the base abstractions
  ([ADR-0133](project-template/0133-abstractions-are-framework-free-and-tiered.md)).
- **Exact keyspace bindings.** Rejected: they need one binding per tenant keyspace.
- **Refusing nested keyspace bindings.** Rejected: nesting is how a host moves one cell or tenant to another server.
- **Checking at options validation with `ValidateOnStart`.** Rejected: it needs a package that is not an abstraction,
  and validators must not create services.
- **The exception message as the span status description.** Rejected: it can carry tenant identifiers, although
  OpenTelemetry suggests it.

## Consequences

- A host with a gap, an overlap or an unmet requirement doesn't start, and one error lists every problem.
- A provider whose server doesn't keep what it declares stops the host's start before any hosted service starts.
- A host moves one cell or tenant to another provider by binding that keyspace.
- A bare service collection calls `AddMetrics()`.
- A provider package implements `ICoordinationProvider` and adds an extension on `ICoordinationBuilder`.
- Dashboards are built against `docs/metrics.md`.

## References

- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1003](1003-providers-are-named-for-the-protocol-they-speak-and-carry-a-profile-per-vendor.md)
- [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
- [ADR-1012](1012-each-primitive-is-an-interface-whose-operations-are-named-with-the-guarantee-dimensions-they-carry.md)
- [ADR-0105](project-template/0105-the-canonical-implementation-is-named-defaultfoo.md)
- [ADR-0113](project-template/0113-metrics-have-one-owner-per-signal-and-bounded-dimensions.md)
- [ADR-0114](project-template/0114-one-activity-source-named-like-its-meter.md)
- [ADR-0132](project-template/0132-implementation-packages-depend-only-on-abstractions.md)
- [ADR-0133](project-template/0133-abstractions-are-framework-free-and-tiered.md)
- OpenTelemetry, "Recording errors" (<https://opentelemetry.io/docs/specs/semconv/general/recording-errors/>), read
  2026-11-25
- OpenTelemetry, "Error attributes" (<https://opentelemetry.io/docs/specs/semconv/registry/attributes/error/>), read
  2026-11-25
- Microsoft Learn, `IHostedLifecycleService`
  (<https://learn.microsoft.com/dotnet/api/microsoft.extensions.hosting.ihostedlifecycleservice>), read 2026-11-25
