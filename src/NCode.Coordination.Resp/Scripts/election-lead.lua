-- Takes the leadership of a role. ARGV[4] is the role's name, ARGV[5] the leader's value, ARGV[6] the new owner token,
-- ARGV[7] the session the leadership is bound to or '' for none, and ARGV[8] the leadership's time to live in
-- milliseconds or 0 when it is bound to a session.
-- Returns {0, owner token, fencing token, value} when the caller leads, either newly or because the same session
-- already led, {1} when another holder leads, and {2} when the session is not live.
local name = ARGV[4]
local field = 'e ' .. name
if ARGV[7] ~= '' and not live(ARGV[7]) then
  return finish({2})
end
local h = holder(field)
if h then
  if ARGV[7] ~= '' and ARGV[7] == h[3] then
    return finish({0, h[1], tonumber(h[2]), redis.call('HGET', grants, 'v ' .. name)})
  end
  return finish({1})
end
drop(field)
local fencing = grant(field, ARGV[6], field, ARGV[7], tonumber(ARGV[8]))
redis.call('HSET', grants, 'v ' .. name, ARGV[5])
return finish({0, ARGV[6], fencing, ARGV[5]})
