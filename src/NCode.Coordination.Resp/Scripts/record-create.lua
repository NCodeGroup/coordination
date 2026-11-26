-- Creates a record when its key is absent. ARGV[4] is the record's member, ARGV[5] its name, ARGV[6] its value and
-- ARGV[7] its time to live in milliseconds, or 0 for a persistent record.
-- Returns {0, revision} when it wrote the record, and {1}, without writing, when the key holds a record.
if find(ARGV[4]) then
  return {1}
end
local revision = advance()
put(ARGV[4], ARGV[5], ARGV[6], tonumber(ARGV[7]), revision)
publish(revision)
return {0, revision}
