CREATE SCHEMA IF NOT EXISTS {schema};
CREATE TABLE {schema}.schema_version (
    version integer PRIMARY KEY,
    compatible_from integer NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE {schema}.keyspaces (
    id bigint PRIMARY KEY,
    name text NOT NULL UNIQUE,
    revision bigint NOT NULL DEFAULT 1,
    log_removed bigint NOT NULL DEFAULT 1
);
INSERT INTO {schema}.schema_version (version, compatible_from) VALUES (1, 1);
