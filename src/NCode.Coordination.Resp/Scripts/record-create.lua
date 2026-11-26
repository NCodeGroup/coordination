-- Creates a record when its key is absent. ARGV[4] is the record's member, ARGV[5] its name, ARGV[6] its value,
-- ARGV[7] its time to live in milliseconds, or 0 for a persistent or session-bound record, and ARGV[8] the session
-- the record is bound to, or '' for a record bound to none. Returns {0, revision} when it wrote the record, and,
-- without writing, {1} when the key holds a record and {2} when the session is not live.
if ARGV[8] ~= '' and not live(ARGV[8]) then
  return {2}
end
if find(ARGV[4]) then
  return {1}
end
local revision = advance()
put(ARGV[4], ARGV[5], ARGV[6], tonumber(ARGV[7]), revision, ARGV[8])
publish(revision)
return {0, revision}
