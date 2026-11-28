CREATE TABLE {schema}.occurrences (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE INDEX occurrences_deadline ON {schema}.occurrences (deadline);
CREATE TABLE {schema}.counters (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    count bigint NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE INDEX counters_deadline ON {schema}.counters (deadline);
CREATE TABLE {schema}.sequences (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE TABLE {schema}.sets (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    key text NOT NULL,
    members integer NOT NULL DEFAULT 0,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth)
);
CREATE INDEX sets_deadline ON {schema}.sets (deadline);
CREATE TABLE {schema}.set_members (
    keyspace_id bigint NOT NULL REFERENCES {schema}.keyspaces (id),
    idx bytea NOT NULL,
    depth smallint NOT NULL,
    member text NOT NULL,
    deadline timestamptz NOT NULL,
    PRIMARY KEY (keyspace_id, idx, depth, member)
);
CREATE INDEX set_members_deadline ON {schema}.set_members (deadline);
INSERT INTO {schema}.schema_version (version, compatible_from) VALUES (2, 1);
