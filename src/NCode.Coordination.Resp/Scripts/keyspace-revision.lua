-- Reads the keyspace's revision, after the prelude has ended expired sessions and dropped lapsed grants.
-- Returns {0, revision}.
return finish({0, string.format('%d', current())})
