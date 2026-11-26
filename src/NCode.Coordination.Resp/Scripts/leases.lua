-- Lease management script: tryAcquire, renew, release, check-held
-- Leases store owner token, sessionId (optional), ttl and fencing term in KEYS[1]:{resourceId}
-- Fencing counters in KEYS[2]:{resourceId}
--
-- KEYS[1]: {keyspace}:leases (the lease prefix)
-- KEYS[2]: {keyspace}:lease-fencing (the fencing counter prefix)
-- ARGV[1]: operation name ('tryAcquire', 'renew', 'release', 'check-held')
-- ARGV[2]: resourceId
-- ARGV[3]: ownerToken (unused on acquire)
-- ARGV[4]: sessionId (unused on renew/release/check)
-- ARGV[5]: ttl in milliseconds
--
-- Returns: {status, [ownerToken], [term], [ttl]} or {status}
-- Status codes: 0 = success, 1 = held/not-held, 2 = session-ended

local operation = ARGV[1]
local resourceId = ARGV[2]
local ownerToken = ARGV[3]
local sessionId = ARGV[4]
local ttl = tonumber(ARGV[5])

local leaseKeyPrefix = KEYS[1]
local fencingCounterPrefix = KEYS[2]

-- Get current server time in milliseconds
local time = redis.call('TIME')
local currentTime = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

-- Check if a session is alive (not expired)
local function isSessionLive(sid)
    if not sid or sid == "" then
        return true  -- TTL-bound leases have no session
    end
    -- Parse out the keyspace from KEYS[1]: "{keyspace}:leases"
    local colonIdx = string.find(leaseKeyPrefix, ':', 1, true)
    local keyspacePart = string.sub(leaseKeyPrefix, 1, colonIdx - 1)
    local sessionsKey = keyspacePart .. ":sessions"
    
    local deadline = redis.call('HGET', sessionsKey, sid)
    if not deadline then
        return false
    end
    deadline = tonumber(deadline)
    return currentTime < deadline
end

if operation == "tryAcquire" then
    local leaseKey = leaseKeyPrefix .. ":" .. resourceId
    local fencingCounterKey = fencingCounterPrefix .. "-" .. resourceId
    
    -- Check if lease is already held
    local existing = redis.call('HGET', leaseKey, 'owner')
    if existing then
        if sessionId ~= "" and existing == sessionId then
            -- Idempotent: same session, return existing grant
            local existingTerm = redis.call('HGET', leaseKey, 'term')
            local existingTtl = redis.call('HGET', leaseKey, 'ttl')
            return {0, existing, existingTerm, existingTtl}
        else
            -- Held by someone else
            return {1}
        end
    end
    
    -- Check session liveness if session-bound
    if sessionId ~= "" and not isSessionLive(sessionId) then
        return {2}  -- session-ended
    end
    
    -- Acquire: increment fencing counter and write lease
    local newTerm = redis.call('INCR', fencingCounterKey)
    local deadline = currentTime + ttl
    
    local ownerVal = sessionId ~= "" and sessionId or ("ttl-" .. tostring(currentTime))
    if ttl > 0 then
        redis.call('HSET', leaseKey, 'owner', ownerVal, 'term', newTerm, 'ttl', ttl, 'deadline', deadline)
        redis.call('PEXPIRE', leaseKey, ttl)
    else
        redis.call('HSET', leaseKey, 'owner', ownerVal, 'term', newTerm, 'ttl', ttl)
    end
    
    return {0, ownerVal, newTerm, ttl}

elseif operation == "renew" then
    local leaseKey = leaseKeyPrefix .. ":" .. resourceId
    
    local existing = redis.call('HGET', leaseKey, 'owner')
    if not existing then
        return {1}  -- not-held
    end
    
    if existing ~= ownerToken then
        return {1}  -- not-held (different owner)
    end
    
    -- Renew: extend TTL
    if ttl > 0 then
        local deadline = currentTime + ttl
        redis.call('HSET', leaseKey, 'ttl', ttl, 'deadline', deadline)
        redis.call('PEXPIRE', leaseKey, ttl)
    else
        redis.call('HSET', leaseKey, 'ttl', ttl)
    end
    
    local term = redis.call('HGET', leaseKey, 'term')
    return {0, existing, term, ttl}

elseif operation == "release" then
    local leaseKey = leaseKeyPrefix .. ":" .. resourceId
    
    local existing = redis.call('HGET', leaseKey, 'owner')
    if not existing then
        return {1}  -- not-held
    end
    
    if existing ~= ownerToken then
        return {1}  -- not-held (different owner)
    end
    
    -- Release: delete lease
    redis.call('DEL', leaseKey)
    return {0}

elseif operation == "check-held" then
    local leaseKey = leaseKeyPrefix .. ":" .. resourceId
    
    local existing = redis.call('HGET', leaseKey, 'owner')
    if not existing then
        return {1}  -- not-held
    end
    
    -- Lease exists; return grant info
    local term = redis.call('HGET', leaseKey, 'term')
    local ttlVal = redis.call('HGET', leaseKey, 'ttl')
    return {0, existing, term, ttlVal}

end

return {3}  -- unknown operation


