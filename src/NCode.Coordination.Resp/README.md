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

`CheckInterval`, 5 seconds by default, sets how often each notification subscription and watch checks its channel's
number, which notices a lost message and, for a watch, deletes and announces the keyspace's expired state. It is more
than zero and less than an hour.

## Profiles

`Profile` names the server, `RespProfiles.Valkey` by default. When the host starts, the provider checks that the
server is that vendor, at that profile's version or later, runs Lua scripts and lets its user run them and every
command the provider sends, and fails the start otherwise.

- **Valkey** 7.2 or later.
- **Garnet** 2.2.13 or later, started with `--lua --lua-transaction-mode`: without them Garnet runs no scripts, or
  runs them without locking their keys, and nothing at startup can tell the second case.

## Primitives

The provider keeps records, record watches, transactions, occurrences, sets, counters, sequences, sessions,
membership, leases, semaphores, elections, claims and notifications. Each operation is one server command or one Lua script, and every time to live is
measured on the server's clock, in whole milliseconds rounded up.

- **Records**: a keyspace keeps its records in one hash, beside an index that lists them under a prefix and the
  deadlines of those with a time to live. Every change is one script that takes the keyspace's next revision and
  publishes it, with the keys it wrote or deleted, on the keyspace's watch channel. A record whose time to live has
  passed is absent at once, and the next script on its keyspace deletes it and announces the delete; a keyspace
  whose records all have a time to live leaves the server an hour after the last of them ends. A page of a listing
  examines at most its limit plus 64 entries of the index, so expired records not yet deleted can leave a page short, or
  empty, with a continuation token; page on until a page has none. A record bound to a session is absent once the
  session ends, and a script on its keyspace deletes it and announces the delete.
- **Transactions**: up to 64 conditions and two branches of up to 64 writes, run as one script on one keyspace's
  records. A put that binds a record to a session that has ended ends the transaction as session ended, with nothing
  written.
- **Record watches**: a watch on a key or a prefix subscribes to its keyspace's watch channel, starts with a resync
  at the keyspace's revision, and reads each written record from the keyspace, delivering it when the record still
  holds that write's revision; a later write of the key arrives with its own event. A delete arrives at its own
  revision. The provider keeps no history, so a watch that names a revision to resume after starts with a resync too.
- **Occurrences**: `SET` with `NX` and a time to live. A call the server doesn't answer throws, so a replay guard
  refuses rather than accepts.
- **Sets**: one sorted set per key, each member scored with its own deadline; at most 10,000 live members.
- **Counters**: the increment that creates a counter sets its time to live, and a batch of up to 16 increments runs
  as one script that stops after the first over its limit. A count is at most 9,007,199,254,740,991 (2^53 − 1), the
  largest integer a Lua number holds exactly; an increment past it fails as `limit-exceeded`.
- **Sequences**: `INCR` and `INCRBY` on a key with no time to live, so a sequence never resets while the server keeps
  its data.
- **Sessions**: a session stores its time to live, so every keep-alive renews it in full. A close or the session's
  expiry ends it at once with the memberships, leases, semaphore slots, leaderships and records bound to it, which
  are absent from then on; each script on the keyspace removes up to 64 of them. A session's id is 128 random bits.
- **Membership**: a group is read at the keyspace's revision, which a join takes, and a leave or a session's end
  takes when it removes a member or a record, and each member's address and metadata come back as written. A join, a
  leave and a session's end name the group in the keyspace's event, and a group's watch lists the group again only
  when an event names it or its channel resyncs.
- **Leases, semaphores and elections**: each grant carries a fencing token from a counter per resource that never
  expires and an owner token of 128 random bits, and is bound to a session or to a time to live. A semaphore has up
  to 1,024 slots, and a leader's value up to 65,536 bytes.
- **Claims**: `SET` with `NX` and the time to live the caller gives, on the claim's name followed by its window's
  segment, so the skew allowance is the caller's, from `TimeWindow.TryCreateTimeToLive`.
- **Waits**: waiting for a session's end or a release, and watching a role, read the server every 100 milliseconds
  until the condition holds or the caller cancels.
- **Notifications**: a publish is one script that takes the topic's next sequence, which never expires, and publishes
  it with the payload, up to 4,096 bytes, on the topic's channel. A subscription starts with a resync carrying 0,
  delivers consecutive sequences, and resyncs carrying the last sequence it delivered.
- **Resyncs**: every subscription and watch reads its channel's messages in order, and resyncs after a gap, a number
  that goes backwards, a dropped and restored connection, or a check, every `CheckInterval`, that finds its channel
  ahead for more than a second. A watch's check reads the keyspace's revision, which deletes and announces its
  expired records and ends its expired sessions, so an expiry reaches the watch within the interval.

The provider declares each of them but record watches and notifications linearizable until the server fails over,
and volatile, since it can't tell whether the server persists or replicates its writes; transactions and counter
batches are atomic across partitions, because a keyspace lives in one slot. It declares record watches linearizable
until a failover, and record watches and notifications at most once with gap detection: a message can be lost, but
never without a resync after it.

## Keyspaces

Every key carries its keyspace as its cluster hash tag, so a keyspace and everything in it live in one slot. A
cluster spreads keyspaces across its shards, not one keyspace: a keyspace per tenant is the usual unit.
