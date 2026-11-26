-- Deletes a record when its revision is the expected one. ARGV[4] is the record's member and ARGV[5] the expected
-- revision. Returns {0} when it deleted the record, and, without writing, {1} when the record has another revision
-- and {2} when the key holds no record.
local record = find(ARGV[4])
if not record then
  return {2}
end
if record[1] ~= ARGV[5] then
  return {1}
end
local revision = advance()
remove(ARGV[4], record[2])
publish(revision)
return {0}
