-- Replaces a record when its revision is the expected one. ARGV[4] is the record's member, ARGV[5] its name, ARGV[6]
-- the expected revision, ARGV[7] the new value and ARGV[8] its time to live in milliseconds, or 0 for a persistent
-- record. Returns {0, revision} when it wrote the record, and, without writing, {1} when the record has another
-- revision and {2} when the key holds no record.
local record = find(ARGV[4])
if not record then
  return {2}
end
if record[1] ~= ARGV[6] then
  return {1}
end
local revision = advance()
put(ARGV[4], ARGV[5], ARGV[7], tonumber(ARGV[8]), revision)
publish(revision)
return {0, revision}
