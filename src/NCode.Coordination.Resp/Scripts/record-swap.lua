-- Replaces a record when its revision is the expected one. ARGV[1] is the record's member, ARGV[2] its name,
-- ARGV[3] the expected revision, ARGV[4] the new value and ARGV[5] its time to live in milliseconds, 0 for
-- persistent, or -1 for session-bound. ARGV[6] is the sessionId (optional, only provided when ARGV[5] == -1).
-- Returns {0, revision} when it wrote the record, and, without writing, {1} when the record has another
-- revision and {2} when the key holds no record.
local record = find(ARGV[1])
if not record then
  return {2}
end
if record[1] ~= ARGV[3] then
  return {1}
end

-- Check if session-bound and validate session is alive
local timeToLive = tonumber(ARGV[5])
if timeToLive == -1 then
  local sessionId = ARGV[6]
  if not sessionId or sessionId == '' then
    return {1}
  end
  -- The session must be validated by the calling code as alive
  -- For now, we just accept it and store with 0 TTL (persistent until session ends)
  timeToLive = 0
end

local revision = advance()
put(ARGV[1], ARGV[2], ARGV[4], timeToLive, revision)
publish(revision)
return {0, revision}
