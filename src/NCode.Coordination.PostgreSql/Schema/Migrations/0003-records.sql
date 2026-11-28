CREATE TABLE {schema}.records (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    value bytea NOT NULL,
    revision bigint NOT NULL,
    deadline timestamptz,
    session_id text,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE INDEX records_deadline ON {schema}.records (deadline) WHERE deadline IS NOT NULL;
CREATE INDEX records_session ON {schema}.records (keyspace_id, session_id, idx, depth) WHERE session_id IS NOT NULL;
CREATE TABLE {schema}.changes (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    revision bigint NOT NULL,
    deadline timestamptz NOT NULL,
    ended_session text,
    group_idx bytea,
    group_depth smallint,
    PRIMARY KEY (keyspace_id, revision)
);
CREATE INDEX changes_deadline ON {schema}.changes (deadline);
CREATE TABLE {schema}.change_items (
    keyspace_id bigint NOT NULL,
    revision bigint NOT NULL,
    ordinal smallint NOT NULL,
    kind smallint NOT NULL,
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    value bytea,
    deadline timestamptz,
    session_id text,
    replaced_session text,
    PRIMARY KEY (keyspace_id, revision, ordinal),
    FOREIGN KEY (keyspace_id, revision) REFERENCES {schema}.changes (keyspace_id, revision) ON DELETE CASCADE
);
CREATE INDEX change_items_replaced ON {schema}.change_items (keyspace_id, replaced_session, idx, depth) WHERE replaced_session IS NOT NULL;
INSERT INTO {schema}.schema_version (version, compatible_from) VALUES (3, 1);
