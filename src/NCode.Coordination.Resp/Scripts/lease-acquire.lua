-- Acquires a lease. ARGV[4] is the resource's name, ARGV[5] the new owner token, ARGV[6] the session the lease is bound
-- to or '' for none, and ARGV[7] the lease's time to live in milliseconds or 0 when it is bound to a session.
-- Returns {0, owner token, fencing token} when the caller holds the lease, either newly or because the same session
-- already held it, {1} when another holder has it, and {2} when the session is not live.
local field = 'l ' .. ARGV[4]
if ARGV[6] ~= '' and not live(ARGV[6]) then
  return finish({2})
end
local h = holder(field)
if h then
  if ARGV[6] ~= '' and ARGV[6] == h[3] then
    return finish({0, h[1], tonumber(h[2])})
  end
  return finish({1})
end
drop(field)
local fencing = grant(field, ARGV[5], field, ARGV[6], tonumber(ARGV[7]))
return finish({0, ARGV[5], fencing})
