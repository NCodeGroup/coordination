-- Election management script: tryLead, resign, read-leader, check-leader
-- Elections store leader's fencing token (term), value and session in {keyspace}:elections:{roleId}
-- Fencing counters per role in {keyspace}:election-fencing-{roleId}
--
-- KEYS[1]: {keyspace}:elections (the elections prefix)
-- KEYS[2]: {keyspace}:election-fencing (the fencing counter prefix)
-- ARGV[1]: operation name ('tryLead', 'resign', 'read-leader', 'check-leader')
-- ARGV[2]: roleId
-- ARGV[3]: value (unused on resign/read/check)
-- ARGV[4]: ownerToken (unused on acquire)
-- ARGV[5]: sessionId (unused on renew/release/check)
-- ARGV[6]: ttl in milliseconds
--
-- Returns: {status, ownerToken, term, value} or {status} or {status, term, value}
-- Status codes: 0 = success, 1 = led-by-another/not-held, 2 = session-ended

local operation = ARGV[1]
local roleId = ARGV[2]
local value = ARGV[3]
local ownerToken = ARGV[4]
local sessionId = ARGV[5]
local ttl = tonumber(ARGV[6])

local electionKeyPrefix = KEYS[1]
local fencingCounterPrefix = KEYS[2]

-- Get current server time in milliseconds
local time = redis.call('TIME')
local currentTime = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

-- Check if a session is alive (not expired)
local function isSessionLive(sid)
    if not sid or sid == "" then
        return true  -- TTL-bound elections have no session
    end
    -- Parse out the keyspace from KEYS[1]: "{keyspace}:elections"
    local colonIdx = string.find(electionKeyPrefix, ':', 1, true)
    local keyspacePart = string.sub(electionKeyPrefix, 1, colonIdx - 1)
    local sessionsKey = keyspacePart .. ":sessions"
    
    local deadline = redis.call('HGET', sessionsKey, sid)
    if not deadline then
        return false
    end
    deadline = tonumber(deadline)
    return currentTime < deadline
end

if operation == "tryLead" then
    local electionKey = electionKeyPrefix .. ":" .. roleId
    
    -- Check session liveness if session-bound
    if sessionId ~= "" and not isSessionLive(sessionId) then
        return {2}  -- session-ended
    end
    
    -- Get current leader info
    local currentLeader = redis.call('HGET', electionKey, 'leader')
    if currentLeader and currentLeader ~= "" then
        local parts = {}
        for part in string.gmatch(currentLeader, "[^|]+") do
            table.insert(parts, part)
        end
        if #parts >= 3 then
            local currentOwner = parts[1]
            local currentTerm = tonumber(parts[2])
            local currentSessionId = parts[3]
            
            -- Idempotent check: if same session already leads, return existing leadership
            if sessionId ~= "" and currentSessionId == sessionId then
                return {0, currentOwner, currentTerm, redis.call('HGET', electionKey, 'value') or ""}
            end
            
            -- Another leader holds it
            return {1}
        end
    end
    
    -- Become the leader: increment fencing counter and write leadership
    local fencingCounterKey = fencingCounterPrefix .. "-" .. roleId
    local newTerm = redis.call('INCR', fencingCounterKey)
    
    local ownerVal = sessionId ~= "" and sessionId or ("ttl-" .. tostring(currentTime))
    local deadline = currentTime + ttl
    
    redis.call('HSET', electionKey, 'leader', ownerVal .. "|" .. newTerm .. "|" .. sessionId, 'value', value, 'deadline', deadline, 'term', newTerm)
    redis.call('PEXPIRE', electionKey, ttl)
    
    return {0, ownerVal, newTerm, value}

elseif operation == "renew" then
    local electionKey = electionKeyPrefix .. ":" .. roleId
    
    local currentLeader = redis.call('HGET', electionKey, 'leader')
    if not currentLeader or currentLeader == "" then
        return {1}  -- not-held
    end
    
    local parts = {}
    for part in string.gmatch(currentLeader, "[^|]+") do
        table.insert(parts, part)
    end
    if #parts < 2 then
        return {1}  -- not-held
    end
    
    local currentOwner = parts[1]
    local currentTerm = tonumber(parts[2])
    
    -- Check owner token matches
    if currentOwner ~= ownerToken then
        return {1}  -- not-held
    end
    
    -- Extend deadline
    local deadline = currentTime + ttl
    redis.call('HSET', electionKey, 'deadline', deadline)
    redis.call('PEXPIRE', electionKey, ttl)
    
    return {0}

elseif operation == "resign" then
    local electionKey = electionKeyPrefix .. ":" .. roleId
    
    local currentLeader = redis.call('HGET', electionKey, 'leader')
    if not currentLeader or currentLeader == "" then
        return {1}  -- not-held
    end
    
    local parts = {}
    for part in string.gmatch(currentLeader, "[^|]+") do
        table.insert(parts, part)
    end
    if #parts < 1 then
        return {1}  -- not-held
    end
    
    local currentOwner = parts[1]
    
    -- Check owner token matches
    if currentOwner ~= ownerToken then
        return {1}  -- not-held
    end
    
    -- Delete leadership
    redis.call('DEL', electionKey)
    
    return {0}

elseif operation == "read-leader" then
    local electionKey = electionKeyPrefix .. ":" .. roleId
    
    local currentLeader = redis.call('HGET', electionKey, 'leader')
    local term = redis.call('HGET', electionKey, 'term')
    local value = redis.call('HGET', electionKey, 'value') or ""
    
    if not currentLeader or currentLeader == "" then
        return {0, 0, ""}  -- no leader
    end
    
    return {0, tonumber(term) or 0, value}

elseif operation == "check-leader" then
    local electionKey = electionKeyPrefix .. ":" .. roleId
    local exists = redis.call('EXISTS', electionKey)
    if exists == 0 then
        return {1}  -- election doesn't exist
    end
    return {0}  -- exists

else
    return redis.error_reply("Unknown operation: " .. operation)
end
