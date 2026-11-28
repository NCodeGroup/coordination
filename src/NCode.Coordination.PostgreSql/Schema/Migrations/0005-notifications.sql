CREATE TABLE {schema}.topics (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    sequence bigint NOT NULL,
    removed bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE TABLE {schema}.messages (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    sequence bigint NOT NULL,
    payload bytea NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth, sequence)
);
CREATE INDEX messages_deadline ON {schema}.messages (deadline);
INSERT INTO {schema}.schema_version (version, compatible_from) VALUES (5, 1);
