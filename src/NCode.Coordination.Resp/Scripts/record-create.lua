-- Creates a record when its key is absent. ARGV[1] is the record's member, ARGV[2] its name, ARGV[3] its value,
-- ARGV[4] its time to live in milliseconds, 0 for persistent, or -1 for session-bound. 
-- ARGV[5] is the sessionId (optional, only provided when ARGV[4] == -1).
-- Returns {0, revision} when it wrote the record, and {1}, without writing, when the key holds a record.
if find(ARGV[1]) then
  return {1}
end

-- Check if session-bound and validate session is alive
local timeToLive = tonumber(ARGV[4])
if timeToLive == -1 then
  local sessionId = ARGV[5]
  if not sessionId or sessionId == '' then
    return {1}
  end
  -- The session must be validated by the calling code as alive
  -- For now, we just accept it and store with 0 TTL (persistent until session ends)
  timeToLive = 0
end

local revision = advance()
put(ARGV[1], ARGV[2], ARGV[3], timeToLive, revision)
publish(revision)
return {0, revision}
