-- Applies counter increments in order, and stops after the first whose count then exceeds its limit.
-- KEYS[i] is the counter of increment i; ARGV[3i-2], ARGV[3i-1] and ARGV[3i] are its amount, its limit and the time
-- to live, in milliseconds, of a counter it creates. Every increment is read and checked before the first write.
-- Returns {0, exceeded, count, ...}: exceeded is the 1-based index of the increment that exceeded its limit, or 0, and
-- a count follows for each applied increment. Returns {1} when a count would pass 2^53 - 1, the largest integer a Lua
-- number holds exactly (ADR-1026).
local largest = 9007199254740991
local counts = {}
local pending = {}
local created = {}
local exceeded = 0
for index = 1, #KEYS do
  local key = KEYS[index]
  local amount = tonumber(ARGV[3 * index - 2])
  local limit = tonumber(ARGV[3 * index - 1])
  local current = pending[key]
  if current == nil then
    local stored = redis.call('GET', key)
    if stored then
      current = tonumber(stored)
    else
      current = 0
      created[key] = ARGV[3 * index]
    end
  end
  if amount > largest - current then
    return {1}
  end
  current = current + amount
  pending[key] = current
  counts[index] = current
  if current > limit then
    exceeded = index
    break
  end
end
for index = 1, #counts do
  redis.call('INCRBY', KEYS[index], ARGV[3 * index - 2])
end
for key, timeToLive in pairs(created) do
  redis.call('PEXPIRE', key, timeToLive)
end
local reply = {0, exceeded}
for index = 1, #counts do
  reply[index + 2] = counts[index]
end
return reply
