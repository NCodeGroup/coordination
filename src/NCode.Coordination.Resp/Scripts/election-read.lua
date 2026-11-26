-- Reads a role's leadership. ARGV[4] is the role's name.
-- Returns {0, fencing token, value} when the role has a leader, and {1} when it has none.
local name = ARGV[4]
local h = holder('e ' .. name)
if h then
  return finish({0, tonumber(h[2]), redis.call('HGET', grants, 'v ' .. name)})
end
return finish({1})
