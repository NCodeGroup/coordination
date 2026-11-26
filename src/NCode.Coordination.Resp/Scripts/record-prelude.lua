-- The start of every record script, which the provider puts before the script's own text when it loads it.
-- KEYS[1] is the keyspace's revision. KEYS[2] holds its records: a hash with three fields per record, 'r:', 'n:' and
-- 'v:' followed by the record's member, which hold its revision, its name and its value. KEYS[3] is the index of the
-- records' members, a sorted set whose scores are all 0, read in lexicographic order. KEYS[4] holds the deadline, in
-- milliseconds on the server's clock, of each record that has a time to live.
-- ARGV[1] is the keyspace's watch channel, ARGV[2] is '1' to publish on it with SPUBLISH and '0' with PUBLISH, and
-- ARGV[3] is how many milliseconds the records outlive their last deadline when none of them is persistent.
-- A record whose deadline is not after the server's current time is absent. Each change takes the keyspace's next
-- revision and publishes an event on the watch channel: the revision, then a line for each record it wrote, 'w' and
-- the record's name, or deleted, 'd' and the record's name, the lines separated by line feeds. The script first
-- deletes up to 64 records whose deadline has passed, as one change.
local revisionKey = KEYS[1]
local records = KEYS[2]
local index = KEYS[3]
local deadlines = KEYS[4]
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local changes = {}

local function current()
  return tonumber(redis.call('GET', revisionKey) or '1')
end

local function find(member)
  local deadline = redis.call('ZSCORE', deadlines, member)
  if deadline and tonumber(deadline) <= now then
    return nil
  end
  local fields = redis.call('HMGET', records, 'r:' .. member, 'n:' .. member, 'v:' .. member)
  if not fields[1] then
    return nil
  end
  return fields
end

local function advance()
  local revision = string.format('%d', current() + 1)
  redis.call('SET', revisionKey, revision)
  return revision
end

local function put(member, name, value, timeToLive, revision)
  redis.call('HSET', records, 'r:' .. member, revision, 'n:' .. member, name, 'v:' .. member, value)
  redis.call('ZADD', index, 0, member)
  if timeToLive > 0 then
    redis.call('ZADD', deadlines, now + timeToLive, member)
  else
    redis.call('ZREM', deadlines, member)
  end
  changes[#changes + 1] = 'w' .. name
end

local function remove(member, name)
  redis.call('HDEL', records, 'r:' .. member, 'n:' .. member, 'v:' .. member)
  redis.call('ZREM', index, member)
  redis.call('ZREM', deadlines, member)
  changes[#changes + 1] = 'd' .. name
end

local function backstop()
  local count = redis.call('ZCARD', index)
  if count == 0 then
    redis.call('DEL', records, index, deadlines)
  elseif count > redis.call('ZCARD', deadlines) then
    redis.call('PERSIST', records)
    redis.call('PERSIST', index)
    redis.call('PERSIST', deadlines)
  else
    local last = redis.call('ZRANGE', deadlines, -1, -1, 'WITHSCORES')
    local timeToLive = string.format('%d', tonumber(last[2]) - now + tonumber(ARGV[3]))
    redis.call('PEXPIRE', records, timeToLive)
    redis.call('PEXPIRE', index, timeToLive)
    redis.call('PEXPIRE', deadlines, timeToLive)
  end
end

local function publish(revision)
  table.insert(changes, 1, revision)
  local event = table.concat(changes, '\n')
  if ARGV[2] == '1' then
    redis.call('SPUBLISH', ARGV[1], event)
  else
    redis.call('PUBLISH', ARGV[1], event)
  end
  changes = {}
  backstop()
end

local expired = redis.call('ZRANGEBYSCORE', deadlines, '-inf', now, 'LIMIT', 0, 64)
if #expired > 0 then
  local revision = advance()
  for i = 1, #expired do
    remove(expired[i], redis.call('HGET', records, 'n:' .. expired[i]))
  end
  publish(revision)
end
