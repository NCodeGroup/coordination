-- Creates a record when its key is absent. ARGV[4] is the record's member, ARGV[5] its name, ARGV[6] its value,
-- ARGV[7] its time to live in milliseconds, 0 for persistent, or -1 for session-bound. 
-- ARGV[8] is the sessionId (optional, only provided when ARGV[7] == -1).
-- Returns {0, revision} when it wrote the record, and {1}, without writing, when the key holds a record.
if find(ARGV[4]) then
  return {1}
end

-- Check if session-bound and validate session is alive
local timeToLive = tonumber(ARGV[7])
local sessionId = ""
if timeToLive == -1 then
  sessionId = ARGV[8]
  if not sessionId or sessionId == '' then
    return {1}
  end
  -- Parse out the keyspace from KEYS[2]: "{keyspace}:{recordTableKey}"
  local colonIdx = string.find(records, ':', 1, true)
  local keyspacePart = string.sub(records, 1, colonIdx - 1)
  local sessionsKey = keyspacePart .. ':sessions'
  
  -- Check session is alive
  local deadline = redis.call('HGET', sessionsKey, sessionId)
  if not deadline then
    return {1}
  end
  local time = redis.call('TIME')
  local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
  if tonumber(deadline) <= now then
    return {1}
  end
  
  -- The record is bound to this session and will live until the session ends
  timeToLive = 0
end

local revision = advance()
put(ARGV[4], ARGV[5], ARGV[6], timeToLive, revision)

-- If session-bound, track this record in a session-bound records set
if sessionId ~= "" then
  local colonIdx = string.find(records, ':', 1, true)
  local keyspacePart = string.sub(records, 1, colonIdx - 1)
  local sessionRecordsKey = keyspacePart .. ':session-records:' .. sessionId
  redis.call('SADD', sessionRecordsKey, ARGV[4])
end

publish(revision)
return {0, revision}
