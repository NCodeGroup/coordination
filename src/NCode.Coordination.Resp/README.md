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

## Keyspaces

Every key carries its keyspace as its cluster hash tag, so a keyspace and everything in it live in one slot. A
cluster spreads keyspaces across its shards, not one keyspace: a keyspace per tenant is the usual unit.
