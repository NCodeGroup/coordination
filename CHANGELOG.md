# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until the first stable release, the [Unreleased] section describes the product as it is, not its changes.

## [Unreleased]

### Added

- Structured keys in NCode.Coordination.Abstractions: a keyspace of a cluster name and typed segments, and keys of text, number and UUID segments with a partition depth, equal by value and matched under a prefix segment by segment ([#6](https://github.com/NCodeGroup/coordination/issues/6)).
- The guarantee vocabulary: consistency, durability, expiry authority and notification delivery, each guarantee defined with the guarantees it implies, and open to guarantees a provider or a consumer defines ([#6](https://github.com/NCodeGroup/coordination/issues/6), [ADR-1002](docs/decisions/1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)).
- Expiry as a validated time to live, measured by the provider from the write, and the conversion of an instant on the caller's clock with an allowance for clock skew ([#6](https://github.com/NCodeGroup/coordination/issues/6), [ADR-1008](docs/decisions/1008-expiry-is-a-duration-on-the-providers-declared-clock-and-an-instant-becomes-one-at-the-caller-with-an-allowance-for-skew.md)).
- Primitive names, a provider's declaration of the guarantees it keeps per primitive or of a primitive it does not support and why, and a feature's requirement of guarantees on a primitive ([#6](https://github.com/NCodeGroup/coordination/issues/6), [ADR-1002](docs/decisions/1002-primitives-declare-guarantees-and-the-composition-refuses-a-requirement-no-provider-meets.md)).

### Security

- Every pull request checks the license of every NuGet dependency, transitive ones included, against the permitted licenses ([#6](https://github.com/NCodeGroup/coordination/issues/6), [ADR-1009](docs/decisions/1009-ci-checks-every-dependencys-license-and-no-snippet-scan-runs.md)).
