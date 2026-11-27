-- Acquires a semaphore slot. ARGV[4] is the semaphore's name, ARGV[5] its capacity, ARGV[6] the new owner token,
-- ARGV[7] the session the slot is bound to or '' for none, and ARGV[8] the slot's time to live in milliseconds or 0
-- when it is bound to a session.
-- Returns {0, slot, owner token, fencing token} when the caller holds a slot, either newly or because the same session
-- already held one, {1} when every slot is held, {2} when the session is not live, and {3} when the semaphore is held
-- with another capacity.
local name, capacity, owner, session, ttl = ARGV[4], tonumber(ARGV[5]), ARGV[6], ARGV[7], tonumber(ARGV[8])
if session ~= '' and not live(session) then
  return finish({2})
end
local stored = tonumber(redis.call('HGET', grants, 'c ' .. name) or capacity)
local free, held = nil, 0
for slot = 0, stored - 1 do
  local h = holder('s ' .. slot .. ' ' .. name)
  if h then
    held = held + 1
    if session ~= '' and h[3] == session and stored == capacity then
      return finish({0, slot, h[1], tonumber(h[2])})
    end
  elseif free == nil then
    free = slot
  end
end
if stored ~= capacity then
  if held > 0 then
    return finish({3})
  end
  for slot = 0, stored - 1 do
    drop('s ' .. slot .. ' ' .. name)
  end
  free = 0
end
if free == nil then
  return finish({1})
end
local field = 's ' .. free .. ' ' .. name
drop(field)
local fencing = grant(field, owner, 's ' .. name, session, ttl)
redis.call('HSET', grants, 'c ' .. name, string.format('%d', capacity))
return finish({0, free, owner, fencing})
