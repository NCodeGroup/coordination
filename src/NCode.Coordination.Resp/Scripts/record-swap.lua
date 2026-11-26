-- Replaces a record when its revision is the expected one. ARGV[4] is the record's member, ARGV[5] its name, ARGV[6]
-- the expected revision, ARGV[7] the new value and ARGV[8] its time to live in milliseconds, or 0 for a persistent
-- record or a session-bound one, and ARGV[9] the session the record is bound to, or '' for a record bound to none.
-- Returns {0, revision} when it wrote the record, and, without writing, {1} when the record has another revision,
-- {2} when the key holds no record and {3} when the session is not live.
if ARGV[9] ~= '' and not live(ARGV[9]) then
  return {3}
end
local record = find(ARGV[4])
if not record then
  return {2}
end
if record[1] ~= ARGV[6] then
  return {1}
end
local revision = advance()
put(ARGV[4], ARGV[5], ARGV[7], tonumber(ARGV[8]), revision, ARGV[9])
publish(revision)
return {0, revision}
