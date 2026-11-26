# NCode.Coordination.Resp

The RESP provider of [NCode.Coordination](https://github.com/NCodeGroup/coordination): coordination over Valkey or
Garnet, through StackExchange.Redis.

## Adding it

```csharp
services.AddCoordination(coordination => coordination
    .AddRespProvider(resp => resp.Configuration = "valkey.internal:6379,ssl=true,password=...")
    .Bind(new ProviderBinding(RespCoordinationBuilderExtensions.ProviderName)));
```

Set `Configuration`, a StackExchange.Redis configuration string, and the provider opens and owns its connection; or
set `Connection` to a factory that returns the host's own `IConnectionMultiplexer`, which the provider uses and never
disposes.

A shared connection keeps its own settings. The provider runs on its default database, reads RESP2 and RESP3 alike,
and works with any backlog policy, reporting a call that times out as possibly applied. Its command map must keep
`EVAL` and `EVALSHA`, or the host's start fails.

## Profiles

`Profile` names the server, `RespProfiles.Valkey` by default. When the host starts, the provider checks that the
server is that vendor, at that profile's version or later, runs Lua scripts and lets its user run them, and fails the
start otherwise.

- **Valkey** 7.2 or later.
- **Garnet** 2.2.13 or later, started with `--lua --lua-transaction-mode`: without them Garnet runs no scripts, or
  runs them without locking their keys, and nothing at startup can tell the second case.

## Primitives

The provider keeps records, transactions, occurrences, sets, counters and sequences. Each operation is one server
command or one Lua script, and every time to live is measured on the server's clock, in whole milliseconds rounded
up.

- **Records**: a keyspace keeps its records in one hash, beside an index that lists them under a prefix and the
  deadlines of those with a time to live. Every change is one script that takes the keyspace's next revision and
  publishes it, with the keys it wrote or deleted, on the keyspace's watch channel. A record whose time to live has
  passed is absent at once, and the next script on its keyspace deletes it and announces the delete; a keyspace
  whose records all have a time to live leaves the server an hour after the last of them ends. Binding a record to
  a session fails as `unsupported`.
- **Transactions**: up to 64 conditions and two branches of up to 64 writes, run as one script on one keyspace's
  records.
- **Occurrences**: `SET` with `NX` and a time to live. A call the server doesn't answer throws, so a replay guard
  refuses rather than accepts.
- **Sets**: one sorted set per key, each member scored with its own deadline; at most 10,000 live members.
- **Counters**: the increment that creates a counter sets its time to live, and a batch of up to 16 increments runs
  as one script that stops after the first over its limit. A count is at most 9,007,199,254,740,991 (2^53 − 1), the
  largest integer a Lua number holds exactly; an increment past it fails as `limit-exceeded`.
- **Sequences**: `INCR` and `INCRBY` on a key with no time to live, so a sequence never resets while the server keeps
  its data.

The provider declares each of them linearizable until the server fails over, and volatile, since it can't tell
whether the server persists or replicates its writes; transactions and counter batches are atomic across
partitions, because a keyspace lives in one slot. It serves no other primitive.

## Keyspaces

Every key carries its keyspace as its cluster hash tag, so a keyspace and everything in it live in one slot. A
cluster spreads keyspaces across its shards, not one keyspace: a keyspace per tenant is the usual unit.
