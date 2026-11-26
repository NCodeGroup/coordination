-- Semaphore management script: tryAcquire (find empty slot), release, renew, check-capacity
-- Semaphores store up to 1,024 slots with owner token, TTL, and fencing term per slot
-- Fencing counters per slot in KEYS[2]:{resourceId}-{slotN}
--
-- KEYS[1]: {keyspace}:semaphores (the semaphore prefix)
-- KEYS[2]: {keyspace}:semaphore-fencing (the fencing counter prefix)
-- ARGV[1]: operation name ('tryAcquire', 'renew', 'release', 'check-capacity')
-- ARGV[2]: resourceId
-- ARGV[3]: capacity (1-1024)
-- ARGV[4]: ownerToken (unused on acquire)
-- ARGV[5]: sessionId (unused on renew/release/check)
-- ARGV[6]: ttl in milliseconds
--
-- Returns: {status, slotIndex, ownerToken, term} or {status}
-- Status codes: 0 = success, 1 = full/not-held, 2 = session-ended, 3 = capacity-conflict

local operation = ARGV[1]
local resourceId = ARGV[2]
local capacity = tonumber(ARGV[3])
local ownerToken = ARGV[4]
local sessionId = ARGV[5]
local ttl = tonumber(ARGV[6])

local semaphoreKeyPrefix = KEYS[1]
local fencingCounterPrefix = KEYS[2]

-- Get current server time in milliseconds
local time = redis.call('TIME')
local currentTime = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

-- Check if a session is alive (not expired)
local function isSessionLive(sid)
    if not sid or sid == "" then
        return true  -- TTL-bound semaphores have no session
    end
    -- Parse out the keyspace from KEYS[1]: "{keyspace}:semaphores"
    local colonIdx = string.find(semaphoreKeyPrefix, ':', 1, true)
    local keyspacePart = string.sub(semaphoreKeyPrefix, 1, colonIdx - 1)
    local sessionsKey = keyspacePart .. ":sessions"
    
    local deadline = redis.call('HGET', sessionsKey, sid)
    if not deadline then
        return false
    end
    deadline = tonumber(deadline)
    return currentTime < deadline
end

if operation == "tryAcquire" then
    local semaphoreKey = semaphoreKeyPrefix .. ":" .. resourceId
    
    -- Check capacity conflict: verify no other holders with different capacity
    local existingCapacity = redis.call('HGET', semaphoreKey, 'capacity')
    if existingCapacity and tonumber(existingCapacity) ~= capacity then
        return {3}  -- capacity-conflict
    end
    
    -- Check session liveness if session-bound
    if sessionId ~= "" and not isSessionLive(sessionId) then
        return {2}  -- session-ended
    end
    
    -- Try to find an empty slot (1 to capacity)
    for slotN = 1, capacity do
        local slotField = "slot-" .. tostring(slotN)
        local existing = redis.call('HGET', semaphoreKey, slotField)
        if not existing then
            -- Idempotent check: if same session already holds this slot, return existing grant
        end
    end
    
    -- Find first empty slot
    local emptySlot = nil
    for slotN = 1, capacity do
        local slotField = "slot-" .. tostring(slotN)
        local existing = redis.call('HGET', semaphoreKey, slotField)
        if not existing then
            emptySlot = slotN
            break
        end
    end
    
    if not emptySlot then
        return {1}  -- full
    end
    
    -- Acquire slot: increment fencing counter and write slot data
    local fencingCounterKey = fencingCounterPrefix .. "-" .. resourceId .. "-" .. tostring(emptySlot)
    local newTerm = redis.call('INCR', fencingCounterKey)
    
    local ownerVal = sessionId ~= "" and sessionId or ("ttl-" .. tostring(currentTime))
    local deadline = currentTime + ttl
    
    local slotField = "slot-" .. tostring(emptySlot)
    redis.call('HSET', semaphoreKey, slotField, ownerVal .. "|" .. newTerm .. "|" .. ttl .. "|" .. deadline, 'capacity', capacity)
    redis.call('PEXPIRE', semaphoreKey, ttl)
    
    return {0, emptySlot, ownerVal, newTerm}

elseif operation == "renew" then
    local semaphoreKey = semaphoreKeyPrefix .. ":" .. resourceId
    local slotN = tonumber(ARGV[7] or "-1")  -- slotN passed as ARGV[7]
    
    if slotN < 1 or slotN > 1024 then
        return {1}  -- not-held
    end
    
    local slotField = "slot-" .. tostring(slotN)
    local slotData = redis.call('HGET', semaphoreKey, slotField)
    
    if not slotData then
        return {1}  -- not-held
    end
    
    -- Parse slot data: ownerVal|term|oldTtl|deadline
    local parts = {}
    for part in string.gmatch(slotData, "[^|]+") do
        table.insert(parts, part)
    end
    
    if #parts < 2 or parts[1] ~= ownerToken then
        return {1}  -- not-held (different owner)
    end
    
    -- Renew: extend TTL
    local deadline = currentTime + ttl
    local newSlotData = ownerToken .. "|" .. parts[2] .. "|" .. ttl .. "|" .. deadline
    redis.call('HSET', semaphoreKey, slotField, newSlotData)
    redis.call('PEXPIRE', semaphoreKey, ttl)
    
    return {0, slotN, ownerToken, parts[2]}

elseif operation == "release" then
    local semaphoreKey = semaphoreKeyPrefix .. ":" .. resourceId
    local slotN = tonumber(ARGV[7] or "-1")  -- slotN passed as ARGV[7]
    
    if slotN < 1 or slotN > 1024 then
        return {1}  -- not-held
    end
    
    local slotField = "slot-" .. tostring(slotN)
    local slotData = redis.call('HGET', semaphoreKey, slotField)
    
    if not slotData then
        return {1}  -- not-held
    end
    
    -- Parse slot data: ownerVal|term|ttl|deadline
    local parts = {}
    for part in string.gmatch(slotData, "[^|]+") do
        table.insert(parts, part)
    end
    
    if #parts < 2 or parts[1] ~= ownerToken then
        return {1}  -- not-held (different owner)
    end
    
    -- Release: delete slot
    redis.call('HDEL', semaphoreKey, slotField)
    return {0}

elseif operation == "check-capacity" then
    local semaphoreKey = semaphoreKeyPrefix .. ":" .. resourceId
    
    local existingCapacity = redis.call('HGET', semaphoreKey, 'capacity')
    if not existingCapacity then
        return {1}  -- not-held
    end
    
    if tonumber(existingCapacity) ~= capacity then
        return {3}  -- capacity-conflict
    end
    
    return {0}

end

return {3}  -- unknown operation
