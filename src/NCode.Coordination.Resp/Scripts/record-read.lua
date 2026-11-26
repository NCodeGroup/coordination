-- Reads a record. ARGV[4] is the record's member.
-- Returns {0, revision, value}, and {1} when the key holds no record.
local record = find(ARGV[4])
if not record then
  return {1}
end
return {0, record[1], record[3]}
