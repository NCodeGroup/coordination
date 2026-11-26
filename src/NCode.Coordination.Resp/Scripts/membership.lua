-- Membership storage and operations.
-- Members of a group are stored in {keyspace}:members hash with format: nodeId -> session:address:metadata
-- A member exists only while its session is live.
--
-- KEYS[1]: {keyspace}:members (hash of members)
-- ARGV[1]: operation name ('join', 'leave', 'list', 'check-liveness')
-- ARGV[2]: group id (for all operations)
-- ARGV[3]: nodeId (for join/leave)
-- ARGV[4]: sessionId (for join/leave)
-- ARGV[5]: address (for join)
-- ARGV[6]: metadata (for join)
--
-- Returns: {status, ...} 
-- Status codes: 0 = success, 1 = taken/ended, 2 = not_found

local operation = ARGV[1]
local groupId = ARGV[2]
local nodeId = ARGV[3]
local sessionId = ARGV[4]
local address = ARGV[5]
local metadata = ARGV[6]

-- Get current server time in milliseconds
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

-- Keys for this group
local groupKey = keyspacePart .. ':members:' .. groupId

-- Extract keyspace from KEYS[1] which is "{keyspace}:members"
local colonIdx = string.find(KEYS[1], ':', 1, true)
local keyspacePart = string.sub(KEYS[1], 1, colonIdx - 1)
local sessionsKey = keyspacePart .. ':sessions'

-- Check if a session is alive
local function isSessionLive(id)
  local deadline = redis.call('HGET', sessionsKey, id)
  if not deadline then
    return false
  end
  return tonumber(deadline) > now
end

-- Join operation
local function join()
  if not isSessionLive(sessionId) then
    return {1}
  end
  
  -- Check if node is already taken by another session
  local existing = redis.call('HGET', groupKey, nodeId)
  if existing then
    local colonPos = string.find(existing, ':')
    local existingSessionId = string.sub(existing, 1, colonPos - 1)
    if existingSessionId ~= sessionId then
      -- Node is taken by another session
      return {1}
    end
  end
  
  -- Store member: sessionId:address:metadata
  local memberData = sessionId .. ':' .. address .. ':' .. (metadata or '')
  redis.call('HSET', groupKey, nodeId, memberData)
  
  return {0}
end

-- Leave operation
local function leave()
  redis.call('HDEL', groupKey, nodeId)
  
  return {0}
end

-- List members operation
local function list()
  local members = redis.call('HGETALL', groupKey)
  local result = {0}
  
  -- Add member count first
  table.insert(result, #members / 2)
  
  -- Add each member with all parts: nodeId, sessionId, address, metadata
  for i = 1, #members, 2 do
    local nId = members[i]
    local data = members[i + 1]
    
    -- Parse data: sessionId:address:metadata
    local colonPos1 = string.find(data, ':')
    local colonPos2 = string.find(data, ':', colonPos1 + 1)
    
    local sid = string.sub(data, 1, colonPos1 - 1)
    local addr = string.sub(data, colonPos1 + 1, colonPos2 - 1)
    local meta = string.sub(data, colonPos2 + 1)
    
    table.insert(result, nId)
    table.insert(result, sid)
    table.insert(result, addr)
    table.insert(result, meta or '')
  end
  
  return result
end

-- Check session liveness (for cleanup)
local function checkLiveness()
  if isSessionLive(sessionId) then
    return {0}
  else
    return {1}
  end
end

-- Main script logic
if operation == 'join' then
  return join()
elseif operation == 'leave' then
  return leave()
elseif operation == 'list' then
  return list()
elseif operation == 'check-liveness' then
  return checkLiveness()
else
  return {2, 'unknown_operation'}
end
