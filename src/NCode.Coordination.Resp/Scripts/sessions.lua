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
  
  -- Use the passed sessionId (from ARGV[2])
  if not sessionId or sessionId == '' then
    return {2, 'invalid_session_id'}
  end
  
  local deadline = now + timeToLive
  
  -- Store session data
  redis.call('HSET', sessionsHash, sessionId, deadline)
  redis.call('ZADD', deadlinesKey, deadline, sessionId)
  
  return {0, sessionId}
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
  -- Parse out the keyspace from KEYS[1]: "{keyspace}:sessions"
  local colonIdx = string.find(sessionsHash, ':', 1, true)
  local keyspacePart = string.sub(sessionsHash, 1, colonIdx - 1)
  
  -- Delete the session itself
  redis.call('HDEL', sessionsHash, sessionId)
  redis.call('ZREM', deadlinesKey, sessionId)
  
  -- Clean up all bound leases
  local leasesPattern = keyspacePart .. ':leases:*'
  local cursor = 0
  repeat
    local scanResult = redis.call('SCAN', cursor, 'MATCH', leasesPattern, 'COUNT', 100)
    cursor = tonumber(scanResult[1])
    local leaseKeys = scanResult[2]
    
    for i = 1, #leaseKeys do
      local leaseKey = leaseKeys[i]
      local leaseData = redis.call('HGET', leaseKey, 'owner')
      if leaseData and leaseData == sessionId then
        redis.call('DEL', leaseKey)
      end
    end
  until cursor == 0
  
  -- Clean up all memberships bound to this sessionId
  local membersKey = keyspacePart .. ':members'
  local members = redis.call('HGETALL', membersKey)
  for i = 1, #members, 2 do
    local nodeId = members[i]
    local memberData = members[i + 1]
    
    -- Parse data: sessionId:address:metadata
    local colonPos = string.find(memberData, ':')
    if colonPos then
      local sid = string.sub(memberData, 1, colonPos - 1)
      if sid == sessionId then
        redis.call('HDEL', membersKey, nodeId)
      end
    end
  end
  
  -- Clean up all election slots with this sessionId
  local electionsPattern = keyspacePart .. ':elections:*'
  cursor = 0
  repeat
    local scanResult = redis.call('SCAN', cursor, 'MATCH', electionsPattern, 'COUNT', 100)
    cursor = tonumber(scanResult[1])
    local electionKeys = scanResult[2]
    
    for i = 1, #electionKeys do
      local electionKey = electionKeys[i]
      local leaderData = redis.call('HGET', electionKey, 'leader')
      if leaderData then
        -- Parse leader data: ownerVal|term|sessionId
        local colonPos1 = string.find(leaderData, '|')
        if colonPos1 then
          local colonPos2 = string.find(leaderData, '|', colonPos1 + 1)
          if colonPos2 then
            local leaderSessionId = string.sub(leaderData, colonPos2 + 1)
            if leaderSessionId == sessionId then
              redis.call('DEL', electionKey)
            end
          end
        end
      end
    end
  until cursor == 0
  
  -- Clean up all semaphore slots with this sessionId
  local semaphoresPattern = keyspacePart .. ':semaphores:*'
  cursor = 0
  repeat
    local scanResult = redis.call('SCAN', cursor, 'MATCH', semaphoresPattern, 'COUNT', 100)
    cursor = tonumber(scanResult[1])
    local semaphoreKeys = scanResult[2]
    
    for i = 1, #semaphoreKeys do
      local semaphoreKey = semaphoreKeys[i]
      local slots = redis.call('HGETALL', semaphoreKey)
      
      for j = 1, #slots, 2 do
        local field = slots[j]
        local value = slots[j + 1]
        
        -- Process slot fields (slot-N format)
        if string.match(field, '^slot%-') then
          -- Parse slot data: ownerVal|term|ttl|deadline
          local colonPos = string.find(value, '|')
          if colonPos then
            local ownerVal = string.sub(value, 1, colonPos - 1)
            if ownerVal == sessionId then
              redis.call('HDEL', semaphoreKey, field)
            end
          end
        end
      end
    end
  until cursor == 0
  
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
