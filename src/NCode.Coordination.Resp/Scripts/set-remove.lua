-- Removes a member from a set. KEYS[1] is the set; ARGV[1] is the member.
-- Returns {0, 1} when the member was live, and {0, 0} when it was absent or past its deadline.
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local score = redis.call('ZSCORE', KEYS[1], ARGV[1])
if not score then
  return {0, 0}
end
redis.call('ZREM', KEYS[1], ARGV[1])
if tonumber(score) > now then
  return {0, 1}
end
return {0, 0}
