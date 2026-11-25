# 1012. Each primitive is an interface whose operations are named with the guarantee dimensions they carry

- **Status:** Proposed
- **Date:** 2026-11-25
- **Deciders:** NCode Group

## Context

[ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md) declares
guarantees per primitive, but a guarantee such as notification delivery means nothing for a read, and the conformance kit
and the history checker need to know which operations a guarantee covers. Providers also differ in the extra settings
they take. [ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md)
rule 4 asks each contract to fit etcd v3 and Consul.

## Decision

**Each primitive is an interface, each operation has a name in an open vocabulary with the guarantee dimensions it
carries, options carry named extensions a provider honors or refuses, and the limits every provider accepts are public
constants.**

1. **One interface per primitive.** Each name in `Primitives.All` has an interface: `ISessionService`,
   `IMembershipService`, `ILeaseService`, `ISemaphoreService`, `IElectionService`, `IClaimService`, `IRecordService`,
   `IRecordWatchService`, `ITransactionService`, `IOccurrenceService`, `ISetService`, `ICounterService`,
   `ISequenceService` and `INotificationService`. Record watches have their own, so a provider can keep records without
   watches.
2. **One method shape.** Every method returns a `ValueTask`, or an `IAsyncEnumerable` for a stream. It takes the state's
   `CoordinationKey`, or a value that carries its keyspace (a session id, a grant, a transaction), so the composition
   routes it by keyspace; then `OperationOptions? options`; then a `CancellationToken`.
3. **Named operations.** Every operation has a name, `<primitive>.<operation>` such as `record.compare-and-swap`, in an
   open vocabulary. `Operations.Definitions` gives each built-in operation's primitive and the guarantee dimensions it
   carries. A guarantee a provider declares for a primitive holds for every operation of it whose definition lists that
   guarantee's dimension, and a requirement names the same guarantees.
4. **Atomicity.** The dimension `atomicity` has the guarantees `atomicity.across-partitions` and
   `atomicity.within-partition`, and the first implies the second. `transaction.execute` and `counter.increment-batch`
   carry it.
5. **Extensions.** `OperationOptions.Extensions` maps an extension name, under the vocabulary-name rule and prefixed by
   its provider such as `consul.lock-delay`, to an invariant-culture text value, at most 32 entries of at most 1,024
   characters. An option every provider understands becomes a property of `OperationOptions`. A provider honors each
   extension it knows and refuses any other as `unsupported`.
6. **Limits.** `CoordinationLimits` holds what every provider accepts: values 65,536 bytes, notification payloads 4,096
   bytes, tokens 128 characters, addresses 256 characters, metadata 64 entries of 512 characters, pages 1,000 records, 64
   conditions and 64 writes per transaction branch, 16 increments per batch, 1,024 semaphore slots, sequence blocks of
   65,536 and 10,000 set members. They fit Consul's 512 KB value limit and 64-operation transactions (read 2026-11-25).
   A provider whose server keeps less declares the primitive unsupported.

## Options considered

- **One interface for every primitive.** Rejected: a provider would implement what it does not support.
- **One request type per operation.** Rejected: it doubles the public surface; recurring input groups are types instead.
- **Guarantees declared per operation.** Rejected: per-primitive declarations suffice once each operation's dimensions
  are data.
- **A typed options class per primitive.** Rejected: no primitive-specific option exists yet.

## Consequences

- The conformance kit iterates `Operations.Definitions`.
- Adding a member to a shipped interface breaks its implementers, so after the first stable release a further operation
  arrives as an additional interface.

## References

- [ADR-1002](1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)
- [ADR-1006](1006-every-provider-passes-one-conformance-kit-that-checks-recorded-histories-against-each-guarantee.md)
- [ADR-1007](1007-the-first-stable-release-carries-the-whole-model-proven-on-three-providers-and-by-its-first-consumer.md)
- [ADR-1011](1011-an-operation-returns-its-outcome-and-throws-a-coded-failure-that-says-whether-it-may-have-taken-effect.md)
- etcd v3.5, "etcd API" (read 2026-11-25)
- Consul HTTP API, KV and transaction pages (read 2026-11-25)
- [Provenance log](../provenance.md)
