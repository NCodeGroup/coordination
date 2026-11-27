# NCode.Coordination.PostgreSql

The PostgreSQL provider of [NCode.Coordination](https://github.com/NCodeGroup/coordination): coordination over
PostgreSQL 15 or later, through Npgsql, in tables of one schema that it migrates.

## Adding it

```csharp
services.AddCoordination(coordination => coordination
    .AddPostgreSqlProvider(postgresql => postgresql.ConnectionString =
        "Host=db.internal;Database=app;Username=coordination;Password=...;SSL Mode=VerifyFull")
    .Bind(new ProviderBinding(PostgreSqlCoordinationBuilderExtensions.ProviderName)));
```

Set `ConnectionString`, an Npgsql connection string, and the provider builds its own data source and disposes it with
the provider; or set `DataSource` to a factory that returns the host's own `NpgsqlDataSource`, which the provider uses
and never disposes. Exactly one of the two is set.

## Connections

- Every connection reaches the primary. A connection string that names several hosts, and a host's data source that
  does, are filtered to the primary, so a write never reaches a standby.
- The provider holds one connection for listening. It must reach PostgreSQL directly or through session pooling, never
  transaction pooling, which doesn't keep a session's `LISTEN`.
- `Multiplexing` must be off.
- No connection enlists in an ambient `TransactionScope`: the provider opens each one outside any ambient
  transaction, and a connection string it builds turns `Enlist` off.
- A connection string it builds keeps its `Keep Alive` setting, or sets 15 seconds when the string leaves it at 0.

## The role

With `ApplyMigrations` on, the default, the role creates the schema, or owns it. Otherwise the role needs `USAGE` on
the schema and `SELECT`, `INSERT`, `UPDATE` and `DELETE` on its tables. The role is never a superuser, and the probe
refuses a role that lacks a privilege it needs.

## Profiles

`Profile` names the deployment, `PostgreSqlProfiles.Asynchronous` by default. When the host starts, the provider
checks what it can of the server and fails the start otherwise. The profile declares the durability the deployment
must make true, and the host chooses the one that its deployment satisfies.

- **Standalone** (`postgresql-standalone`) declares `durability.durable`, for one server whose commits reach disk.
  It needs `fsync` on and `synchronous_commit` not `off` for the provider's role.
- **Asynchronous** (`postgresql-asynchronous`) declares `durability.replicated-asynchronously`, for a primary whose
  standbys copy commits after it acknowledges them, so a failover may lose commits. It needs the same settings as
  Standalone, and is the default because its declaration is true of the most deployments.
- **Synchronous** (`postgresql-synchronous`) declares `durability.durable`, for a primary that waits for a standby
  before it acknowledges a commit. It also needs a standby in `synchronous_standby_names` and `synchronous_commit` set
  to `on` or `remote_apply`, set for the provider's role.

A service whose replication the probe can't see takes a profile of the host's own: `new PostgreSqlProfile(name,
durability, requiresSynchronousStandby)`. The probe still checks `fsync` and `synchronous_commit`.

## The schema

`Schema` names the schema that holds the tables, `coordination` by default: 1 to 63 lowercase letters, digits and
underscores, starting with a letter or an underscore.

With `ApplyMigrations` on, the provider applies each missing migration at start, each in its own transaction under an
advisory lock, so hosts that start together don't race. A host that applies schema changes itself sets
`ApplyMigrations = false` and runs the scripts of `PostgreSqlSchema.GetMigrations(schema)` in order, each once, in its
own transaction. The probe refuses a schema it can't use: one that is missing, or whose version this release can't
work with.

## Keyspaces

A keyspace is a row whose lock orders the changes that watches can see, so the changes of one keyspace are numbered
one after another and those of different keyspaces don't wait for each other. A keyspace per tenant is the unit of
scale.

## Timeouts

`StatementTimeout`, `LockTimeout` and `IdleInTransactionTimeout`, 5 seconds each by default, are set with `SET LOCAL`
in every operation's transaction, so they bind that operation and not the connection that the pool keeps. Each is at
least a millisecond and less than an hour. A call that exceeds one fails as `unavailable`, and says whether it may have
taken effect.
