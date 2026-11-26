-- Adds a member to a set: a sorted set whose scores are its members' deadlines, in milliseconds on the server's clock.
-- KEYS[1] is the set; ARGV[1] is the member, ARGV[2] its time to live in milliseconds and ARGV[3] the most live members.
-- Returns {0, 1} when the member was absent, {0, 0} when it was live, and {1}, without writing, when the set is full.
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local timeToLive = tonumber(ARGV[2])
local score = redis.call('ZSCORE', KEYS[1], ARGV[1])
local present = score and tonumber(score) > now
if not present then
  local live = redis.call('ZCOUNT', KEYS[1], '(' .. now, '+inf')
  if live >= tonumber(ARGV[3]) then
    return {1}
  end
end
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
redis.call('ZADD', KEYS[1], now + timeToLive, ARGV[1])
if redis.call('PTTL', KEYS[1]) < timeToLive then
  redis.call('PEXPIRE', KEYS[1], ARGV[2])
end
if present then
  return {0, 0}
end
return {0, 1}
