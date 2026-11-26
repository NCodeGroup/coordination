-- Sessions storage and operations.
-- Sessions store their TTL, liveness check timestamp and bound leases in {keyspace}:sessions:{sessionId},
-- indexed by expiry in {keyspace}:sessions-deadlines.
--
-- KEYS[1]: {keyspace}:sessions (the sessions hash key)
-- ARGV[1]: operation name ('open', 'keepAlive', 'close', 'check-liveness')
-- ARGV[2]: sessionId (for operations other than 'open')
-- ARGV[3]: timeToLive in milliseconds (for 'open' and 'keepAlive')
--
-- Returns: {status, [sessionId]} or {status, error_code}
-- Status codes: 0 = success, 1 = ended, 2 = not_found

local operation = ARGV[1]
local sessionId = ARGV[2]
local timeToLive = tonumber(ARGV[3])

-- Get current server time in milliseconds
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

-- Sessions hash holds: {sessionId} -> deadline
local sessionsHash = KEYS[1]
local deadlinesKey = KEYS[1] .. ':deadlines'

-- Check if a session is alive (not expired)
local function isLive(id)
  local deadline = redis.call('HGET', sessionsHash, id)
  if not deadline then
    return false
  end
  return tonumber(deadline) > now
end

-- Clean up expired sessions
local function cleanupExpired()
  local expired = redis.call('ZRANGEBYSCORE', deadlinesKey, '-inf', now)
  for i = 1, #expired do
    local id = expired[i]
    redis.call('HDEL', sessionsHash, id)
  end
  redis.call('ZREMRANGEBYSCORE', deadlinesKey, '-inf', now)
end

-- Open a new session
local function openSession()
  if not timeToLive or timeToLive <= 0 then
    return {2, 'invalid_ttl'}
  end
  
  -- Generate a simple sessionId
  local counter = redis.call('INCR', 'session-counter')
  local id = 's-' .. tostring(math.floor(now / 1000)) .. '-' .. tostring(counter)
  
  local deadline = now + timeToLive
  
  -- Store session data
  redis.call('HSET', sessionsHash, id, deadline)
  redis.call('ZADD', deadlinesKey, deadline, id)
  
  return {0, id}
end

-- Keep session alive
local function keepAlive()
  if not isLive(sessionId) then
    return {1}
  end
  
  local deadline = now + timeToLive
  
  redis.call('HSET', sessionsHash, sessionId, deadline)
  redis.call('ZADD', deadlinesKey, deadline, sessionId)
  
  return {0}
end

-- Close a session
local function closeSession()
  -- Delete the session (whether it exists or not, this is idempotent)
  redis.call('HDEL', sessionsHash, sessionId)
  redis.call('ZREM', deadlinesKey, sessionId)
  
  return {0}
end

-- Check if a session is live (for membership and lease queries)
local function checkLiveness()
  local live = isLive(sessionId)
  if live then
    return {0}
  else
    return {1}
  end
end

-- Main script logic
cleanupExpired()

if operation == 'open' then
  return openSession()
elseif operation == 'keepAlive' then
  return keepAlive()
elseif operation == 'close' then
  return closeSession()
elseif operation == 'check-liveness' then
  return checkLiveness()
else
  return {2, 'unknown_operation'}
end
