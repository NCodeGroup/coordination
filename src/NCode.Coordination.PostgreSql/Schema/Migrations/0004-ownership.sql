CREATE TABLE {schema}.sessions (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    id text NOT NULL,
    ttl_us bigint NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, id)
);
CREATE INDEX sessions_deadline ON {schema}.sessions (deadline);
CREATE TABLE {schema}.ended_sessions (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    session_id text NOT NULL,
    revision bigint NOT NULL,
    PRIMARY KEY (keyspace_id, session_id)
);
CREATE TABLE {schema}.members (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    group_idx bytea NOT NULL,
    group_depth smallint NOT NULL,
    group_key text NOT NULL,
    session_id text NOT NULL,
    node_id text NOT NULL,
    address text NOT NULL,
    metadata_names text[] NOT NULL,
    metadata_values text[] NOT NULL,
    PRIMARY KEY (keyspace_id, group_idx, group_depth, session_id)
);
CREATE INDEX members_session ON {schema}.members (keyspace_id, session_id);
CREATE TABLE {schema}.grants (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    kind smallint NOT NULL,
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    slot integer NOT NULL,
    capacity integer NOT NULL,
    fencing bigint NOT NULL,
    owner text NOT NULL,
    session_id text,
    deadline timestamptz,
    value bytea,
    PRIMARY KEY (keyspace_id, kind, idx, depth, slot)
);
CREATE INDEX grants_deadline ON {schema}.grants (deadline) WHERE deadline IS NOT NULL;
CREATE INDEX grants_session ON {schema}.grants (keyspace_id, session_id) WHERE session_id IS NOT NULL;
CREATE TABLE {schema}.fencing (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    kind smallint NOT NULL,
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    last bigint NOT NULL,
    PRIMARY KEY (keyspace_id, kind, idx, depth)
);
CREATE TABLE {schema}.claims (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE INDEX claims_deadline ON {schema}.claims (deadline);
INSERT INTO {schema}.schema_version (version, compatible_from) VALUES (4, 1);
