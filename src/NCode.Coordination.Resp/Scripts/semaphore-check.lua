-- Checks whether a semaphore has a free slot. ARGV[4] is the semaphore's name.
-- Returns {0} when every slot is held, and {1} when the semaphore has a free slot or is not held at all.
local stored = redis.call('HGET', grants, 'c ' .. ARGV[4])
if not stored then
  return finish({1})
end
for slot = 0, tonumber(stored) - 1 do
  if not holder('s ' .. slot .. ' ' .. ARGV[4]) then
    return finish({1})
  end
end
return finish({0})
