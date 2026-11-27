-- Removes a session's member from a group. ARGV[4] is the group and ARGV[5] the session. Takes the next revision and
-- publishes it, with a line 'm' and the group, when a member was removed. Returns {0}, whether or not the session had a
-- member in the group.
local group, session = ARGV[4], ARGV[5]
local node = redis.call('HGET', memberFields, 'n ' .. group .. ' ' .. session)
if not node then
  return finish({0})
end
local revision = advance()
dropMember(group, node)
changes[#changes + 1] = 'm' .. group
publish(revision)
return {0}
