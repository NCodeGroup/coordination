-- Reads a record and deletes it. ARGV[4] is the record's member.
-- Returns {0, revision, value} with the record it deleted, and {1} when the key holds no record.
local record = find(ARGV[4])
if not record then
  return {1}
end
local revision = advance()
remove(ARGV[4], record[2])
publish(revision)
return {0, record[1], record[3]}
