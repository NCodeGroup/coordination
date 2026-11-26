-- Claim management script: tryClaim, check-claimed
-- Claims store holder and deadline in {keyspace}:claims (hash)
-- Index by deadline: {keyspace}:claims-deadlines (sorted set)
-- No tokens, no release; expiry from name:window to window end plus skew allowance
--
-- KEYS[1]: {keyspace}:claims (the claims hash)
-- KEYS[2]: {keyspace}:claims-deadlines (the claims deadline index)
-- ARGV[1]: operation name ('tryClaim', 'check-claimed')
-- ARGV[2]: name
-- ARGV[3]: window (start:end, e.g. "1000:2000")
-- ARGV[4]: ttl in milliseconds
-- ARGV[5]: currentTime (server time in ms, unused; server TIME is used)
-- ARGV[6]: skew in milliseconds
--
-- Returns: {status} 
-- Status codes: 0 = claimed, 1 = taken

local operation = ARGV[1]
local name = ARGV[2]
local window = ARGV[3]
local ttl = tonumber(ARGV[4])
local skew = tonumber(ARGV[6])

local claimsKey = KEYS[1]
local deadlineIndexKey = KEYS[2]

-- Get current server time in milliseconds
local time = redis.call('TIME')
local currentTime = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

if operation == "tryClaim" then
    local claimField = name .. ":" .. window
    
    -- Delete expired claims from the deadline index
    redis.call('ZREMRANGEBYSCORE', deadlineIndexKey, '-inf', currentTime)
    
    -- Check if name:window already claimed
    local existing = redis.call('HGET', claimsKey, claimField)
    if existing and existing ~= "" then
        return {1}  -- taken
    end
    
    -- Parse window to get end time
    local parts = {}
    for part in string.gmatch(window, "[^:]+") do
        table.insert(parts, part)
    end
    if #parts < 2 then
        return redis.error_reply("Invalid window format: " .. window)
    end
    
    local windowStart = tonumber(parts[1])
    local windowEnd = tonumber(parts[2])
    
    -- Claim deadline = window end + skew
    local deadline = windowEnd + skew
    
    -- Write claim (holder is opaque per implementation, we store a marker)
    redis.call('HSET', claimsKey, claimField, 'claimed')
    
    -- Add to deadline index
    redis.call('ZADD', deadlineIndexKey, deadline, claimField)
    
    -- Set expiry on the claims hash (use max TTL to deadline)
    local expireMs = deadline - currentTime
    if expireMs > 0 then
        redis.call('PEXPIRE', claimsKey, expireMs)
    end
    
    return {0}  -- claimed

elseif operation == "check-claimed" then
    local claimField = name .. ":" .. window
    
    -- Delete expired claims
    redis.call('ZREMRANGEBYSCORE', deadlineIndexKey, '-inf', currentTime)
    
    -- Check if name:window claimed
    local existing = redis.call('HGET', claimsKey, claimField)
    if existing and existing ~= "" then
        return {1}  -- taken
    end
    
    return {0}  -- not claimed

else
    return redis.error_reply("Unknown operation: " .. operation)
end
